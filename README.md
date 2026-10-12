# پاپی | Papey — فروشگاه تیشرت (Blazor + .NET 10)

Desktop printing integration: [PRINTING_API.md](PRINTING_API.md) — paid-order queue, SignalR notifications, receipt URLs, and client protocol.

فروشگاه اینترنتی تیشرت **پاپی** (Papey.ir) با Blazor Server روی .NET 10، مشابه tshock.ir

## امکانات

- 🏠 صفحه اصلی با هیرو، دسته‌بندی‌ها، جدیدترین‌ها و محبوب‌ترین‌ها (طراحی مشابه tshock.ir، راست‌چین و فارسی)
- 👕 صفحه محصول با انتخاب **رنگ** (۶ رنگ)، **سایز** (S تا XXL) و **تعداد** با کنترل موجودی
- 🛒 سبد خرید کامل (افزودن، تغییر تعداد، حذف) با ذخیره‌سازی پایدار در کش توزیع‌شده
- 📦 تسویه حساب با آدرس گیرنده (استان/شهر/آدرس/کدپستی) + **ذخیره آدرس** در حساب کاربری
- 💳 درگاه پرداخت **زرین‌پال** (سندباکس) با callback و verify + idempotency
- 📱 ارسال **SMS به ادمین‌ها** (چند شماره) پس از خرید موفق با **KaveNegar**
- 🔐 احراز هویت کوکی (ثبت‌نام/ورود/خروج) + نقش Admin
- ⚙️ **پنل مدیریت**: داشبورد، مدیریت محصولات (افزودن/فعال/غیرفعال/حذف)، مدیریت سفارش‌ها (تغییر وضعیت)، دسته‌بندی‌ها
- 🐘 دیتابیس **PostgreSQL** + EF Core (auto-migrate & seed)
- 🐳 **Docker** کامل (app + db با docker-compose)
- 🖼 تصاویر محصول به صورت SVG محلی در `wwwroot/img/products`

## راه‌اندازی سریع (Docker)

```bash
cp .env.example .env   # مقادیر را تنظیم کنید
docker compose up -d --build
```

سپس مرورگر را باز کنید: `http://localhost:8080`

## راه‌اندازی محلی (بدون Docker)

```bash
docker compose up -d db   # فقط Postgres
dotnet run --urls http://localhost:5032
```

## تنظیمات (.env)

| متغیر | توضیح |
|---|---|
| `POSTGRES_USER` / `POSTGRES_PASSWORD` / `POSTGRES_DB` | اتصال PostgreSQL |
| `DB_HOST` / `DB_PORT` | هاست/پورت دیتابیس (لوکال: `localhost:5434`) |
| `ZARINPAL_MERCHANT_ID` | مرچنت‌کد زرین‌پال (سندباکس: همه صفر) |
| `ZARINPAL_SANDBOX` | `true` برای سندباکس |
| `ZARINPAL_CALLBACK_URL` | آدرس callback — Docker: `http://localhost:8080/payment/callback` (لوکال بدون Docker: `http://localhost:5032/payment/callback`) |
| `KAVENEGAR_API_KEY` | توکن KaveNegar |
| `KAVENEGAR_SENDER` | شماره خط ارسال (اختیاری) |
| `ADMIN_PHONES` | شماره‌های ادمین، با ویرگول جدا شوند |
| `ADMIN_USERNAME` / `ADMIN_PASSWORD` / `ADMIN_EMAIL` | حساب ادمین اولیه |

## حساب ادمین پیش‌فرض (seed)

- نام کاربری: **Admin**
- رمز عبور: **@Admin1234**

> ⚠️ در محیط واقعی حتماً رمز را در `.env` تغییر دهید و دیتابیس را ریست کنید.

## ساختار پروژه

```
Papey/
├── Components/
│   ├── Layout/          # MainLayout (فروشگاه) + AdminLayout (پنل مدیریت)
│   ├── Pages/           # صفحات فروشگاه + Admin/
│   └── ProductGrid.razor
├── Data/                # AppDbContext + DbSeeder
├── Models/              # Category, Product, ProductVariant, AppUser, Address, Order, OrderItem, CartItem
├── Services/            # ZarinpalService, SmsService (KaveNegar), CartService, AuthService, PersianHelpers
├── Migrations/          # EF Core migrations
├── wwwroot/img/products # تصاویر SVG محصولات
├── Dockerfile
├── docker-compose.yml
└── .env(.example)
```

## تست

تمام جریان خرید به‌صورت end-to-end تست شده است:
خانه ← محصول (انتخاب رنگ/سایز/تعداد) ← سبد خرید ← تسویه ← زرین‌پال سندباکس ← پرداخت موفق ← ثبت سفارش (پرداخت‌شده) ← نمایش در پنل مدیریت.
