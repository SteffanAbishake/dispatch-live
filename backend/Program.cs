using Dispatch;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Threading.RateLimiting;
var builder=WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<Store>(o=>o.UseSqlite(builder.Configuration.GetConnectionString("Database")));
builder.Services.AddAuthentication("Session").AddScheme<AuthenticationSchemeOptions,SessionAuth>("Session",null);
builder.Services.AddAuthorization(o=>o.AddPolicy("ApprovedDriver",p=>p.RequireRole("Driver").RequireClaim("approved","true")));
builder.Services.AddSignalR();builder.Services.AddHttpClient();builder.Services.AddSingleton<Otp>();builder.Services.AddHostedService<ExpiryWorker>();
builder.Services.AddRateLimiter(o=>{o.RejectionStatusCode=429;o.AddPolicy("auth",ctx=>RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString()??"unknown",_=>new FixedWindowRateLimiterOptions{PermitLimit=15,Window=TimeSpan.FromMinutes(1),QueueLimit=0}));});
builder.Services.Configure<ForwardedHeadersOptions>(o=>{o.ForwardedHeaders=ForwardedHeaders.XForwardedFor|ForwardedHeaders.XForwardedProto;var proxy=builder.Configuration["ReverseProxy:Address"];if(!string.IsNullOrEmpty(proxy))o.KnownProxies.Add(System.Net.IPAddress.Parse(proxy));});
var app=builder.Build();Directory.CreateDirectory("data");
using(var scope=app.Services.CreateScope()){
 var db=scope.ServiceProvider.GetRequiredService<Store>();await db.Database.EnsureCreatedAsync();
 if(!await db.Users.AnyAsync(u=>u.Role=="Admin")){
  var email=builder.Configuration["Bootstrap:Email"]?.Trim().ToLowerInvariant();var password=builder.Configuration["Bootstrap:Password"];
  if(string.IsNullOrWhiteSpace(email)||password is null||password.Length<16)throw new InvalidOperationException("Set Bootstrap__Email and Bootstrap__Password (at least 16 characters) before first launch.");
  db.Users.Add(new DriverUser{CompanyId=Guid.NewGuid().ToString(),Role="Admin",Name=builder.Configuration["Bootstrap:Company"]??"Dispatch",Email=email,PasswordHash=Security.Password(password),Approved=true});await db.SaveChangesAsync();
 }
}
app.UseForwardedHeaders();
app.Use(async(ctx,next)=>{try{
 ctx.Response.Headers.CacheControl="no-store";ctx.Response.Headers["X-Content-Type-Options"]="nosniff";ctx.Response.Headers["Referrer-Policy"]="no-referrer";
 if(ctx.Request.Path.StartsWithSegments("/api")&&ctx.Request.Method!="GET"&&!ctx.Request.Headers.Authorization.ToString().StartsWith("Bearer ")){
  if(ctx.Request.Headers["X-Dispatch-Request"]!="1"){ctx.Response.StatusCode=403;await ctx.Response.WriteAsJsonAsync(new{error="Missing request protection header."});return;}
  var origin=ctx.Request.Headers.Origin.ToString();if(!string.IsNullOrEmpty(origin)&&(!Uri.TryCreate(origin,UriKind.Absolute,out var url)||url.Authority!=ctx.Request.Host.Value)){ctx.Response.StatusCode=403;await ctx.Response.WriteAsJsonAsync(new{error="Invalid origin."});return;}
 }await next();
 }catch(ApiError e){ctx.Response.StatusCode=e.Status;await ctx.Response.WriteAsJsonAsync(new{error=e.Message});}catch(BadHttpRequestException){ctx.Response.StatusCode=400;await ctx.Response.WriteAsJsonAsync(new{error="Invalid request."});}catch(Exception e){app.Logger.LogError(e,"Request failed");ctx.Response.StatusCode=500;await ctx.Response.WriteAsJsonAsync(new{error="The request could not be completed."});}});
if(!app.Environment.IsDevelopment()){app.UseHsts();if(!builder.Configuration.GetValue<bool>("Hosting:HttpsTerminatedAtEdge"))app.UseHttpsRedirection();}
app.UseDefaultFiles();app.UseStaticFiles();app.UseRouting();app.UseRateLimiter();app.UseAuthentication();app.UseAuthorization();
app.MapGet("/health",()=>new{status="ok"});
var auth=app.MapGroup("/api/auth").RequireRateLimiting("auth");
auth.MapPost("/admin",async(AdminLogin p,Store db,HttpContext ctx)=>{
 var email=p.Email?.Trim().ToLowerInvariant();var u=await db.Users.SingleOrDefaultAsync(u=>u.Email==email&&u.Role=="Admin"&&u.Enabled);if(u is null||!Security.Verify(p.Password??"",u.PasswordHash))return Results.Json(new{error="Invalid credentials."},statusCode:401);
 var token=Security.Token();db.Sessions.Add(new LoginSession{UserId=u.Id,TokenHash=Security.Hash(token),ExpiresAt=Security.Now+12*3600000});await db.SaveChangesAsync();ctx.Response.Cookies.Append("dispatch_session",token,new CookieOptions{HttpOnly=true,Secure=!app.Environment.IsDevelopment(),SameSite=SameSiteMode.Strict,MaxAge=TimeSpan.FromHours(12),Path="/"});return Results.Ok(new{name=u.Name});
});
auth.MapPost("/otp/request",async(PhoneInput p,Store db,Otp otp)=>{if(!Security.Phone(p.Phone))throw new ApiError(400,"Use an international phone number, for example +94771234567.");var u=await db.Users.SingleOrDefaultAsync(u=>u.Phone==p.Phone&&u.Role=="Driver"&&u.Enabled);if(u is not null)await otp.Send(db,p.Phone);return Results.Ok(new{message="If this number was invited, a verification code has been sent."});});
auth.MapPost("/otp/verify",async(VerifyInput p,Store db,Otp otp)=>{
 if(!Security.Phone(p.Phone)||p.Code is null||p.Code.Length!=6||!p.Code.All(char.IsAsciiDigit))throw new ApiError(400,"Enter the six-digit code.");
 var user=await db.Users.SingleOrDefaultAsync(u=>u.Phone==p.Phone&&u.Enabled&&u.Role=="Driver");if(user is null||!await otp.Check(db,p.Phone,p.Code))throw new ApiError(401,"Invalid or expired code.");
 var token=Security.Token();db.Sessions.Add(new LoginSession{UserId=user.Id,TokenHash=Security.Hash(token),ExpiresAt=Security.Now+24*3600000});await db.SaveChangesAsync();return Results.Ok(new{token,user.Name,user.Approved,expiresInSeconds=86400});
});
app.MapPost("/api/auth/logout",async(ClaimsPrincipal u,Store db,HttpContext ctx)=>{await Tracking.End(db.Deliveries.Where(d=>d.DriverId==u.U()&&d.AuthSessionId==u.S()),"Signed out");await db.Sessions.Where(s=>s.Id==u.S()).ExecuteDeleteAsync();ctx.Response.Cookies.Delete("dispatch_session");return Results.Ok();}).RequireAuthorization();
var admin=app.MapGroup("/api/admin").RequireAuthorization(new AuthorizeAttribute{Roles="Admin"});
admin.MapGet("/drivers",async(ClaimsPrincipal u,Store db)=>await db.Users.Where(x=>x.CompanyId==u.C()&&x.Role=="Driver").Select(x=>new{x.Id,x.Name,x.Phone,x.Approved,x.Enabled}).ToListAsync());
admin.MapPost("/drivers",async(InviteInput p,ClaimsPrincipal u,Store db)=>{if(string.IsNullOrWhiteSpace(p.Name)||p.Name.Length>60||!Security.Phone(p.Phone))throw new ApiError(400,"Enter a name and international phone number.");if(await db.Users.AnyAsync(x=>x.Phone==p.Phone))throw new ApiError(409,"That phone number is already registered.");var d=new DriverUser{Name=p.Name.Trim(),Phone=p.Phone,CompanyId=u.C()};db.Users.Add(d);await db.SaveChangesAsync();return Results.Ok(new{d.Id,d.Name,d.Approved});});
admin.MapPatch("/drivers/{id}",async(string id,ApprovalInput p,ClaimsPrincipal u,Store db)=>{var d=await db.Users.SingleOrDefaultAsync(x=>x.Id==id&&x.CompanyId==u.C()&&x.Role=="Driver");if(d is null)return Results.NotFound();d.Approved=p.Approved;await db.SaveChangesAsync();if(!p.Approved)await Tracking.End(db.Deliveries.Where(x=>x.DriverId==id&&x.CompanyId==u.C()),"Approval withdrawn");return Results.Ok();});
admin.MapGet("/deliveries",async(ClaimsPrincipal u,Store db)=>{var rows=await(from d in db.Deliveries.AsNoTracking() join v in db.Users on d.DriverId equals v.Id where d.CompanyId==u.C() orderby d.ConsentAt descending select new{d,driverName=v.Name,phone=v.Phone}).Take(200).ToListAsync();return rows.Select(x=>new{x.d.Id,x.driverName,x.phone,x.d.Reference,x.d.Active,status=Tracking.Status(x.d,Security.Now),x.d.Latitude,x.d.Longitude,x.d.Accuracy,x.d.CapturedAt,x.d.ReceivedAt,x.d.EndReason});});
admin.MapPost("/deliveries/{id}/end",async(string id,ClaimsPrincipal u,Store db,IHubContext<DispatchHub> hub)=>{if(!await db.Deliveries.AnyAsync(d=>d.Id==id&&d.CompanyId==u.C()))return Results.NotFound();await Tracking.End(db.Deliveries.Where(d=>d.Id==id&&d.CompanyId==u.C()),"Ended by dispatcher");await hub.Clients.Group(u.C()).SendAsync("changed");return Results.Ok();});
var driver=app.MapGroup("/api/driver").RequireAuthorization(new AuthorizeAttribute{Roles="Driver"});
driver.MapGet("/me",async(ClaimsPrincipal u,Store db)=>{var user=await db.Users.SingleAsync(x=>x.Id==u.U());var delivery=await db.Deliveries.Where(d=>d.DriverId==u.U()&&d.CompanyId==u.C()&&d.Active).Select(d=>new{d.Id,d.Reference,d.ExpiresAt}).SingleOrDefaultAsync();return Results.Ok(new{user.Name,user.Approved,delivery});});
driver.MapPost("/deliveries",async(StartInput p,ClaimsPrincipal u,Store db)=>{
 if(!p.Consent||p.ConsentVersion!="location-v1"||string.IsNullOrWhiteSpace(p.Reference)||p.Reference.Length>80)throw new ApiError(400,"Agree to location sharing and enter a delivery reference.");
 if(await db.Deliveries.AnyAsync(d=>d.DriverId==u.U()&&d.Active))throw new ApiError(409,"End the previous delivery before starting a new one.");
 var d=new Delivery{DriverId=u.U(),CompanyId=u.C(),AuthSessionId=u.S(),Reference=p.Reference.Trim(),ConsentAt=Security.Now,ExpiresAt=Security.Now+8*3600000};db.Deliveries.Add(d);try{await db.SaveChangesAsync();}catch(DbUpdateException){throw new ApiError(409,"An active delivery already exists.");}return Results.Ok(new{d.Id,d.ExpiresAt});
}).RequireAuthorization("ApprovedDriver");
driver.MapPut("/deliveries/{id}/location",async(string id,FixInput p,ClaimsPrincipal u,Store db,IHubContext<DispatchHub> hub)=>{
 var now=Security.Now;if(!Tracking.Valid(p,now))throw new ApiError(400,"Location is invalid or too old.");
 var changed=await db.Deliveries.Where(d=>d.Id==id&&d.CompanyId==u.C()&&d.DriverId==u.U()&&d.AuthSessionId==u.S()&&d.Active&&d.ExpiresAt>now&&d.Sequence<p.Sequence&&(d.CapturedAt==null||d.CapturedAt<=p.CapturedAt)).ExecuteUpdateAsync(s=>s.SetProperty(d=>d.Latitude,p.Latitude).SetProperty(d=>d.Longitude,p.Longitude).SetProperty(d=>d.Accuracy,p.Accuracy).SetProperty(d=>d.CapturedAt,p.CapturedAt).SetProperty(d=>d.ReceivedAt,now).SetProperty(d=>d.Sequence,p.Sequence));
 if(changed==0)throw new ApiError(409,"Session ended, expired, belongs to another device, or update is out of order.");await hub.Clients.Group(u.C()).SendAsync("changed");return Results.Ok();
}).RequireAuthorization("ApprovedDriver");
driver.MapPost("/deliveries/{id}/end",async(string id,ClaimsPrincipal u,Store db,IHubContext<DispatchHub> hub)=>{if(!await db.Deliveries.AnyAsync(d=>d.Id==id&&d.DriverId==u.U()&&d.CompanyId==u.C()))return Results.NotFound();await Tracking.End(db.Deliveries.Where(d=>d.Id==id&&d.DriverId==u.U()&&d.CompanyId==u.C()),"Driver stopped sharing");await hub.Clients.Group(u.C()).SendAsync("changed");return Results.Ok();});
app.MapHub<DispatchHub>("/hubs/dispatch");app.Run();
