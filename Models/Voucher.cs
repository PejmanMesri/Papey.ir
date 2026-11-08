using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace Papey.Models;
public class Voucher {
 public int Id {get;set;}
 [MaxLength(40)] public string Code {get;set;}="";
 public int Percent {get;set;}
 [Column(TypeName="numeric(18,0)")] public decimal MaxReduction {get;set;}
 public DateTime ExpiresAt {get;set;}
 public int UseLimit {get;set;}
 public bool Active {get;set;}=true;
 public int CreatedByUserId {get;set;}
 public DateTime CreatedAt {get;set;}=DateTime.UtcNow;
}
public class VoucherUse {
 public int Id {get;set;}
 public int VoucherId {get;set;}
 public Voucher Voucher {get;set;}=null!;
 public int UserId {get;set;}
 public int OrderId {get;set;}
 public Order Order {get;set;}=null!;
}
