using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
namespace Dispatch;
public class Otp(IConfiguration config,IHostEnvironment environment,IHttpClientFactory clients,ILogger<Otp> log){
 bool Development=>environment.IsDevelopment()&&config.GetValue<bool>("Otp:DevelopmentCodes");
 public async Task Send(Store db,string phone){
  var c=await db.Challenges.FindAsync(phone);if(c is not null&&Security.Now-c.SentAt<60000)throw new ApiError(429,"Wait a minute before requesting another code.");
  c??=new PhoneChallenge{Phone=phone};if(db.Entry(c).State==EntityState.Detached)db.Challenges.Add(c);c.SentAt=Security.Now;c.ExpiresAt=Security.Now+300000;c.Attempts=0;
  if(Development){var code=RandomNumberGenerator.GetInt32(100000,1000000).ToString();c.CodeHash=Security.Hash(phone+code);await db.SaveChangesAsync();log.LogWarning("DEVELOPMENT ONLY OTP {Phone}: {Code}",phone,code);}
  else{await Call("Verifications",new(){["To"]=phone,["Channel"]="sms"});c.CodeHash="provider";await db.SaveChangesAsync();}
 }
 public async Task<bool> Check(Store db,string phone,string code){
  var now=Security.Now;var changed=await db.Challenges.Where(x=>x.Phone==phone&&x.ExpiresAt>now&&x.Attempts<5).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Attempts,x=>x.Attempts+1));if(changed==0)return false;
  var c=await db.Challenges.AsNoTracking().SingleAsync(x=>x.Phone==phone);var valid=Development?c.CodeHash==Security.Hash(phone+code):(await Call("VerificationCheck",new(){["To"]=phone,["Code"]=code})).GetProperty("status").GetString()=="approved";
  return valid&&await db.Challenges.Where(x=>x.Phone==phone&&x.SentAt==c.SentAt).ExecuteDeleteAsync()==1;
 }
 async Task<JsonElement> Call(string resource,Dictionary<string,string> body){
  var sid=config["Otp:AccountSid"];var secret=config["Otp:AuthToken"];var service=config["Otp:ServiceSid"];
  if(string.IsNullOrWhiteSpace(sid)||string.IsNullOrWhiteSpace(secret)||string.IsNullOrWhiteSpace(service))throw new ApiError(503,"SMS verification is not configured.");
  var client=clients.CreateClient();client.Timeout=TimeSpan.FromSeconds(15);using var request=new HttpRequestMessage(HttpMethod.Post,$"https://verify.twilio.com/v2/Services/{Uri.EscapeDataString(service)}/{resource}");request.Headers.Authorization=new AuthenticationHeaderValue("Basic",Convert.ToBase64String(Encoding.ASCII.GetBytes(sid+":"+secret)));request.Content=new FormUrlEncodedContent(body);
  using var response=await client.SendAsync(request);if(!response.IsSuccessStatusCode)throw new ApiError(503,"SMS verification failed. Please retry later.");using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());return json.RootElement.Clone();
 }
}
