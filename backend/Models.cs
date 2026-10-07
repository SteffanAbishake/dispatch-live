using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
namespace Dispatch;
public class DriverUser {
 public string Id {get;set;}=Guid.NewGuid().ToString(); public string CompanyId {get;set;}=""; public string Name {get;set;}="";
 public string Role {get;set;}="Driver"; public string? Phone {get;set;} public string? Email {get;set;} public string? PasswordHash {get;set;}
 public bool Approved {get;set;} public bool Enabled {get;set;}=true;
}
public class LoginSession {public string Id {get;set;}=Guid.NewGuid().ToString();public string UserId {get;set;}="";public string TokenHash {get;set;}="";public long ExpiresAt {get;set;}}
public class Delivery {
 public string Id {get;set;}=Guid.NewGuid().ToString();public string CompanyId {get;set;}="";public string DriverId {get;set;}="";public string AuthSessionId {get;set;}="";
 public string Reference {get;set;}="";public bool Active {get;set;}=true;public string EndReason {get;set;}="";
 public long ConsentAt {get;set;}public string ConsentVersion {get;set;}="location-v1";public long ExpiresAt {get;set;}public long? EndedAt {get;set;}
 public double? Latitude {get;set;}public double? Longitude {get;set;}public double? Accuracy {get;set;}
 public long? CapturedAt {get;set;}public long? ReceivedAt {get;set;}public long Sequence {get;set;}=-1;
}
public class PhoneChallenge {public string Phone {get;set;}="";public string CodeHash {get;set;}="";public long SentAt {get;set;}public long ExpiresAt {get;set;}public int Attempts {get;set;}}
public class Store(DbContextOptions<Store> options):DbContext(options){
 public DbSet<DriverUser> Users=>Set<DriverUser>();public DbSet<LoginSession> Sessions=>Set<LoginSession>();public DbSet<Delivery> Deliveries=>Set<Delivery>();public DbSet<PhoneChallenge> Challenges=>Set<PhoneChallenge>();
 protected override void OnModelCreating(ModelBuilder m){m.Entity<DriverUser>().HasIndex(x=>x.Phone).IsUnique();m.Entity<DriverUser>().HasIndex(x=>x.Email).IsUnique();m.Entity<DriverUser>().HasIndex(x=>x.CompanyId);m.Entity<LoginSession>().HasIndex(x=>x.TokenHash).IsUnique();m.Entity<Delivery>().HasIndex(x=>new{x.DriverId,x.Active}).IsUnique().HasFilter("Active = 1");m.Entity<Delivery>().HasIndex(x=>new{x.CompanyId,x.Active});m.Entity<PhoneChallenge>().HasKey(x=>x.Phone);}
}
public static class Security {
 public static long Now=>DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
 public static string Hash(string s)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
 public static string Token()=>Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
 public static bool Phone(string? p)=>p is not null&&System.Text.RegularExpressions.Regex.IsMatch(p,@"^\+[1-9]\d{7,14}$");
 public static string Password(string value){var salt=RandomNumberGenerator.GetBytes(16);var hash=Rfc2898DeriveBytes.Pbkdf2(value,salt,600000,HashAlgorithmName.SHA256,32);return Convert.ToBase64String(salt)+":"+Convert.ToBase64String(hash);}
 public static bool Verify(string value,string? saved){try{var p=saved!.Split(':');return CryptographicOperations.FixedTimeEquals(Rfc2898DeriveBytes.Pbkdf2(value,Convert.FromBase64String(p[0]),600000,HashAlgorithmName.SHA256,32),Convert.FromBase64String(p[1]));}catch{return false;}}
}
public record PhoneInput(string Phone);
public record VerifyInput(string Phone,string Code);
public record AdminLogin(string Email,string Password);
public record InviteInput(string Name,string Phone);
public record ApprovalInput(bool Approved);
public record StartInput(string Reference,bool Consent,string ConsentVersion);
public record FixInput(double Latitude,double Longitude,double Accuracy,long CapturedAt,long Sequence);
public class ApiError(int status,string message):Exception(message){public int Status=>status;}
