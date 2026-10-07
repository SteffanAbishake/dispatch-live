using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;
namespace Dispatch;
[Authorize(Roles="Admin")]
public class DispatchHub:Hub {public override async Task OnConnectedAsync(){await Groups.AddToGroupAsync(Context.ConnectionId,Context.User!.C());await base.OnConnectedAsync();}}
public static class Tracking {
 public static bool Valid(FixInput p,long now)=>double.IsFinite(p.Latitude)&&p.Latitude>=-90&&p.Latitude<=90&&double.IsFinite(p.Longitude)&&p.Longitude>=-180&&p.Longitude<=180&&double.IsFinite(p.Accuracy)&&p.Accuracy>=0&&p.Accuracy<=100000&&p.CapturedAt>now-120000&&p.CapturedAt<=now+30000&&p.Sequence>=0;
 public static string Status(Delivery d,long now)=>!d.Active?"Ended":now>=d.ExpiresAt?"Expired":d.CapturedAt is null?"Waiting for GPS":now-d.CapturedAt>45000||now-d.ReceivedAt>45000?"Location unavailable":"Live";
 public static Task<int> End(IQueryable<Delivery> query,string reason)=>query.Where(d=>d.Active).ExecuteUpdateAsync(s=>s.SetProperty(d=>d.Active,false).SetProperty(d=>d.EndReason,reason).SetProperty(d=>d.EndedAt,Security.Now).SetProperty(d=>d.Latitude,(double?)null).SetProperty(d=>d.Longitude,(double?)null).SetProperty(d=>d.Accuracy,(double?)null).SetProperty(d=>d.CapturedAt,(long?)null));
}
public class ExpiryWorker(IServiceScopeFactory scopes,ILogger<ExpiryWorker> log):BackgroundService{
 protected override async Task ExecuteAsync(CancellationToken token){while(!token.IsCancellationRequested){try{using var scope=scopes.CreateScope();var db=scope.ServiceProvider.GetRequiredService<Store>();await Tracking.End(db.Deliveries.Where(d=>d.ExpiresAt<Security.Now),"Session expired");await db.Sessions.Where(s=>s.ExpiresAt<Security.Now).ExecuteDeleteAsync();await db.Challenges.Where(s=>s.ExpiresAt<Security.Now).ExecuteDeleteAsync();}catch(Exception e){log.LogError(e,"Expiry cleanup failed");}try{await Task.Delay(TimeSpan.FromMinutes(1),token);}catch(OperationCanceledException){break;}}}
}
