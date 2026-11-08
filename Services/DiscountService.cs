using Microsoft.EntityFrameworkCore;
using Papey.Data;
using Papey.Models;
using System.Security.Claims;
namespace Papey.Services;
public sealed record DiscountQuote(int StorePercent,decimal StoreAmount,string? Code,int VoucherPercent,decimal VoucherAmount,string? Error=null) {
 public decimal Total=>StoreAmount+VoucherAmount;
}
public sealed class DiscountService(IDbContextFactory<AppDbContext> factory) {
 public static string Normalize(string? code)=>PersianHelpers.ToEnglishDigits(code??"").Trim().ToUpperInvariant();
 public static decimal PercentOff(decimal subtotal,int percent)=>decimal.Round(Math.Max(0,subtotal)*Math.Clamp(percent,0,100)/100m,0,MidpointRounding.AwayFromZero);
 public static DiscountQuote Calculate(decimal subtotal,int storePercent,Voucher? voucher=null,string? error=null) {
  var store=PercentOff(subtotal,storePercent);
  var amount=voucher is null?0:Math.Min(voucher.MaxReduction,PercentOff(subtotal-store,voucher.Percent));
  return new(storePercent,store,voucher?.Code,voucher?.Percent??0,amount,error);
 }
 public async Task<DiscountQuote> QuoteAsync(decimal subtotal,string? code,int? uid) {
  await using var db=await factory.CreateDbContextAsync();
  var settings=await db.StoreSettings.AsNoTracking().SingleAsync(s=>s.Id==1);
  return await QuoteAsync(db,subtotal,code,uid,settings.DiscountPercent,false);
 }
 public static async Task<DiscountQuote> QuoteAsync(AppDbContext db,decimal subtotal,string? code,int? uid,int storePercent,bool reserve) {
  code=Normalize(code);if(code.Length==0)return Calculate(subtotal,storePercent);
  if(uid is null)return Calculate(subtotal,storePercent,error:"برای کد تخفیف وارد حساب شوید.");
  var v=reserve?(await db.Vouchers.FromSqlInterpolated($"""SELECT * FROM "Vouchers" WHERE "Code" = {code} FOR UPDATE""").ToListAsync()).SingleOrDefault():await db.Vouchers.AsNoTracking().SingleOrDefaultAsync(v=>v.Code==code);
  if(v is null||!v.Active||v.ExpiresAt<=DateTime.UtcNow)return Calculate(subtotal,storePercent,error:"کد تخفیف معتبر نیست، غیرفعال یا منقضی شده است.");
  if(await db.VoucherUses.AnyAsync(u=>u.VoucherId==v.Id&&u.UserId==uid))return Calculate(subtotal,storePercent,error:"این کد را قبلاً استفاده کرده‌اید.");
  if(await db.VoucherUses.CountAsync(u=>u.VoucherId==v.Id)>=v.UseLimit)return Calculate(subtotal,storePercent,error:"ظرفیت استفاده از این کد تمام شده است.");
  return Calculate(subtotal,storePercent,v);
 }
 public async Task<string?> SaveAsync(ClaimsPrincipal? principal,Voucher draft) {
  await using var db=await factory.CreateDbContextAsync();
  if(!int.TryParse(principal?.FindFirstValue(ClaimTypes.NameIdentifier),out var uid))return "دسترسی ندارید.";
  var user=await db.Users.FindAsync(uid);if(!AccountAccess.Valid(principal!,user,true)||!user!.CanManageStore)return "دسترسی ندارید.";
  draft.Code=Normalize(draft.Code);
  if(!System.Text.RegularExpressions.Regex.IsMatch(draft.Code,@"^[A-Z0-9_-]{3,40}$"))return "کد باید ۳ تا ۴۰ حرف انگلیسی، عدد، خط تیره یا زیرخط باشد.";
  if(draft.Percent<1||draft.Percent>(user.IsAdmin?100:30))return user.IsAdmin?"درصد بین ۱ تا ۱۰۰ باشد.":"فروشنده حداکثر ۳۰٪ تخفیف می‌تواند تعریف کند.";
  if(draft.MaxReduction<1||draft.MaxReduction!=decimal.Truncate(draft.MaxReduction)||draft.UseLimit<1||draft.ExpiresAt<=DateTime.UtcNow)return "سقف تخفیف، تعداد استفاده و تاریخ آینده را وارد کنید.";
  if(await db.Vouchers.AnyAsync(v=>v.Code==draft.Code))return "این کد قبلاً تعریف شده است.";
  draft.Id=0;draft.CreatedByUserId=uid;draft.CreatedAt=DateTime.UtcNow;db.Vouchers.Add(draft);
  try{await db.SaveChangesAsync();}catch(DbUpdateException){return "ذخیره انجام نشد؛ ممکن است کد تکراری باشد.";}return null;
 }
 public async Task<bool> DeactivateAsync(ClaimsPrincipal? principal,int id) {
  await using var db=await factory.CreateDbContextAsync();if(!int.TryParse(principal?.FindFirstValue(ClaimTypes.NameIdentifier),out var uid))return false;
  var user=await db.Users.FindAsync(uid);if(!AccountAccess.Valid(principal!,user,true)||!user!.CanManageStore)return false;
  return await db.Vouchers.Where(v=>v.Id==id).ExecuteUpdateAsync(v=>v.SetProperty(x=>x.Active,false))==1;
 }
}
