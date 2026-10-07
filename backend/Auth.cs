using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Encodings.Web;
namespace Dispatch;
public class SessionAuth(IOptionsMonitor<AuthenticationSchemeOptions> options,ILoggerFactory logger,UrlEncoder encoder,Store db):AuthenticationHandler<AuthenticationSchemeOptions>(options,logger,encoder){
 protected override async Task<AuthenticateResult> HandleAuthenticateAsync(){
  string? token=null;var header=Request.Headers.Authorization.ToString();if(header.StartsWith("Bearer ",StringComparison.Ordinal))token=header[7..];else Request.Cookies.TryGetValue("dispatch_session",out token);
  if(string.IsNullOrEmpty(token)||token.Length!=64)return AuthenticateResult.NoResult();var hash=Security.Hash(token);
  var session=await db.Sessions.AsNoTracking().SingleOrDefaultAsync(s=>s.TokenHash==hash&&s.ExpiresAt>Security.Now);if(session is null)return AuthenticateResult.Fail("Session expired");
  var user=await db.Users.AsNoTracking().SingleOrDefaultAsync(u=>u.Id==session.UserId&&u.Enabled);if(user is null)return AuthenticateResult.Fail("Account disabled");
  var claims=new[]{new Claim(ClaimTypes.NameIdentifier,user.Id),new Claim(ClaimTypes.Role,user.Role),new Claim("company",user.CompanyId),new Claim("session",session.Id),new Claim("approved",user.Approved?"true":"false")};
  return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims,Scheme.Name)),Scheme.Name));
 }
}
public static class Identity {public static string U(this ClaimsPrincipal u)=>u.FindFirstValue(ClaimTypes.NameIdentifier)!;public static string C(this ClaimsPrincipal u)=>u.FindFirstValue("company")!;public static string S(this ClaimsPrincipal u)=>u.FindFirstValue("session")!;}
