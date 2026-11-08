using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Papey.Data;
using Papey.Models;
using Papey.Services;
int checks=0;void Check(bool value,string message){if(!value)throw new Exception(message);checks++;Console.WriteLine("PASS: "+message);}
var source=new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PAPEY_TEST_CONNECTION")??throw new Exception("Disposable test connection required")){Pooling=false};
var name="papey_vouchers_"+Guid.NewGuid().ToString("N");await using var admin=new NpgsqlConnection(new NpgsqlConnectionStringBuilder(source.ConnectionString){Database="postgres"}.ConnectionString);await admin.OpenAsync();await new NpgsqlCommand($"CREATE DATABASE \"{name}\"",admin).ExecuteNonQueryAsync();source.Database=name;
var options=new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(source.ConnectionString).Options;var factory=new Factory(options);
try{
 await using var db=factory.CreateDbContext();await db.Database.MigrateAsync();
 var owner=new AppUser{Username="owner",IsAdmin=true,PasswordHash=BCrypt.Net.BCrypt.HashPassword("owner-test")};var staff=new AppUser{Username="staff",IsStaff=true,PasswordHash="unused"};var customer=new AppUser{Username="09120000001",Phone="09120000001",PasswordHash=BCrypt.Net.BCrypt.HashPassword("oldpass")};db.Users.AddRange(owner,staff,customer);await db.SaveChangesAsync();
 var discounts=new DiscountService(factory);
 Voucher Draft(string code,int percent=20,int count=5)=>new(){Code=code,Percent=percent,UseLimit=count,MaxReduction=50000,ExpiresAt=DateTime.UtcNow.AddDays(2)};
 Check(await discounts.SaveAsync(AccountAccess.Principal(staff),Draft("too-high",31)) is not null,"Seller cannot define more than 30 percent on the server");
 Check(await discounts.SaveAsync(AccountAccess.Principal(staff),Draft("seller-30",30)) is null,"Seller can create a 30 percent voucher");
 Check(await discounts.SaveAsync(AccountAccess.Principal(customer),Draft("customer")) is not null,"Customer cannot define vouchers");
 Check(await discounts.SaveAsync(AccountAccess.Principal(owner),Draft(" welcome ",50,1)) is null,"Owner can create higher discounts and code is normalized");
 Check(await discounts.SaveAsync(AccountAccess.Principal(owner),Draft("WELCOME")) is not null,"Case-insensitive duplicate is rejected");
 Check(await discounts.SaveAsync(AccountAccess.Principal(owner),new Voucher{Code="EXPIRED",Percent=10,MaxReduction=1,UseLimit=1,ExpiresAt=DateTime.UtcNow.AddDays(-1)}) is not null,"Expired vouchers cannot be created");
 await db.StoreSettings.Where(s=>s.Id==1).ExecuteUpdateAsync(s=>s.SetProperty(s=>s.DiscountPercent,10));
 var quote=await discounts.QuoteAsync(1000000,"wElCoMe",customer.Id);
 Check(quote.StoreAmount==100000&&quote.VoucherAmount==50000&&quote.Error is null,"Global discount is applied first, voucher percentage is capped");
 var order=new Order{OrderNumber="CHECK",UserId=customer.Id,TotalAmount=1000000,StoreDiscountAmount=quote.StoreAmount,StoreDiscountPercent=10,VoucherCode=quote.Code,VoucherDiscountAmount=quote.VoucherAmount};OrderTax.Apply(order,true);
 Check(order.TaxAmount==85000&&order.GrandTotal==935000,"Tax and payable total use discounted subtotal");
 // Two customers compete for the final voucher slot; the row lock must serialize reservations.
 var voucher=await db.Vouchers.SingleAsync(v=>v.Code=="WELCOME");
 async Task<bool> Reserve(int uid){await using var ctx=factory.CreateDbContext();await using var tx=await ctx.Database.BeginTransactionAsync();var q=await DiscountService.QuoteAsync(ctx,1000000,"welcome",uid,10,true);if(q.Error is not null)return false;var o=new Order{OrderNumber="RACE-"+uid,UserId=uid,TotalAmount=1000000};ctx.Orders.Add(o);ctx.VoucherUses.Add(new VoucherUse{VoucherId=voucher.Id,UserId=uid,Order=o});await ctx.SaveChangesAsync();await Task.Delay(50);await tx.CommitAsync();return true;}
 var result=await Task.WhenAll(Reserve(customer.Id),Reserve(owner.Id));Check(result.Count(x=>x)==1,"Concurrent checkout cannot exceed voucher use limit");
 var used=await db.VoucherUses.SingleAsync(u=>u.VoucherId==voucher.Id);
 Check((await discounts.QuoteAsync(1000000,"welcome",used.UserId)).Error is not null,"A customer cannot use the same voucher twice");
 var sellerVoucher=await db.Vouchers.SingleAsync(v=>v.Code=="SELLER-30");Check(await discounts.DeactivateAsync(AccountAccess.Principal(staff),sellerVoucher.Id),"Seller can deactivate vouchers");Check((await discounts.QuoteAsync(100000,"seller-30",customer.Id)).Error is not null,"Inactive code is rejected");
 await db.StoreSettings.Where(s=>s.Id==1).ExecuteUpdateAsync(s=>s.SetProperty(s=>s.DiscountPercent,0));Check(order.GrandTotal==935000&&order.StoreDiscountAmount==100000,"Historical order amounts remain independent of current settings");
 order.Items=[new(){LineTotal=333333},new(){LineTotal=666667}];Check(OrderTax.Allocate(order).Sum()==order.TaxAmount&&OrderTax.AllocateDiscount(order).Sum()==order.DiscountAmount,"Official invoice line allocations reconcile to saved tax and discounts");
 var config=new ConfigurationBuilder().AddInMemoryCollection().Build();var env=new Env();var sms=new SmsService(new HttpClient(new NoNetwork()),config,env,NullLogger<SmsService>.Instance);
 AuthService Auth(ClaimsPrincipal p)=>new(factory,new HttpContextAccessor{HttpContext=new DefaultHttpContext{User=p}},config,sms,env);
 var phoneAuth=Auth(AccountAccess.Principal(customer));Check((await phoneAuth.ValidateLoginAsync("۰۹۱۲۰۰۰۰۰۰۱","oldpass"))?.Id==customer.Id,"Password login accepts Persian phone digits");
 Check((await phoneAuth.UpdateProfileAsync(customer.Id,"Customer",null,"myusername")).Success,"OTP-created customer can choose username once");
 Check(!(await phoneAuth.UpdateProfileAsync(customer.Id,"Customer",null,"secondname")).Success,"Custom username cannot be overwritten later");
 Check((await phoneAuth.ValidateLoginAsync("MYUSERNAME","oldpass"))?.Id==customer.Id,"Chosen username supports case-insensitive password login");
 Check(!(await phoneAuth.ChangePasswordAsync(customer.Id,"wrong","newpass1")).Success,"Password-authenticated session still needs old password");
 var otpPrincipal=AccountAccess.Principal(customer);((ClaimsIdentity)otpPrincipal.Identity!).AddClaim(new(ClaimTypes.AuthenticationMethod,"otp"));var otpAuth=Auth(otpPrincipal);
 Check((await otpAuth.ChangePasswordAsync(customer.Id,"","newpass1")).Success,"Verified OTP session can set password without old password");
 Check(sms.CustomNotificationPhones.SequenceEqual(new[]{"09122197962"}),"Custom-order SMS audience is restricted to requested administrator");
 Console.WriteLine($"{checks} voucher, accounting and account checks passed; no SMS, payment or print calls.");
}finally{await new NpgsqlCommand($"DROP DATABASE \"{name}\" WITH (FORCE)",admin).ExecuteNonQueryAsync();}
sealed class Factory(DbContextOptions<AppDbContext> options):IDbContextFactory<AppDbContext>{public AppDbContext CreateDbContext()=>new(options);}
sealed class NoNetwork:HttpMessageHandler{protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>throw new Exception("Network forbidden");}
sealed class Env:IHostEnvironment{public string EnvironmentName{get;set;}="Production";public string ApplicationName{get;set;}="Checks";public string ContentRootPath{get;set;}=Directory.GetCurrentDirectory();public IFileProvider ContentRootFileProvider{get;set;}=new NullFileProvider();}
