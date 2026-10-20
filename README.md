# پاپی | Papey — فروشگاه تیشرت (Blazor + .NET 10)

Desktop printing integration: [PRINTING_API.md](PRINTING_API.md) — paid-order queue, SignalR notifications, receipt URLs, and client protocol.

فروشگاه اینترنتی تیشرت **پاپی** (Papey.ir) با Blazor Server روی .NET 10، مشابه tshock.ir

## امکانات

- 🏠 صفحه اصلی با هیرو، دسته‌بندی‌ها، جدیدترین‌ها و محبوب‌ترین‌ها (طراحی مشابه tshock.ir، راست‌چین و فارسی)
- 👕 صفحه محصول با انتخاب **رنگ** (۶ رنگ)، **سایز** (S تا XXL) و **تعداد** با کنترل موجودی
- 🛒 سبد خرید کامل (افزودن، تغییر تعداد، حذف) با ذخیره‌سازی پایدار در کش توزیع‌شده
- 📦 تسویه حساب با آدرس گیرنده (استان/شهر/آدرس/کدپستی) + **ذخیره آدرس** در حساب کاربری
- 💳 درگاه پرداخت **زرین‌پال** (سندباکس) با callback و verify + idempotency
- 📱 ارسال **SMS به ادمین‌ها** (چند شماره) پس از خرید موفق با **SMS.ir**
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
| `SMSIR_API_KEY` | کلید SMS.ir محیط اصلی |
| `SMSIR_SANDBOX_API_KEY` | کلید محیط آزمایشی |
| `SMSIR_SANDBOX` | حالت آزمایشی (در توسعه همیشه فعال) |
| `SMSIR_LINE_NUMBER` | شماره خط ارسال گروهی |
| `SMSIR_OTP_TEMPLATE_ID` | شناسه قالب تأییدشده ورود با پارامتر CODE |
| `SMSIR_SHIPPING_TEMPLATE_ID` | شناسه قالب تأییدشده ارسال سفارش با پارامتر ORDERNUMBER |
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
├── Services/            # ZarinpalService, SmsService (SMS.ir), CartService, AuthService, PersianHelpers
├── Migrations/          # EF Core migrations
├── wwwroot/img/products # تصاویر SVG محصولات
├── Dockerfile
├── docker-compose.yml
└── .env(.example)
```

## تست

تمام جریان خرید به‌صورت end-to-end تست شده است:
خانه ← محصول (انتخاب رنگ/سایز/تعداد) ← سبد خرید ← تسویه ← زرین‌پال سندباکس ← پرداخت موفق ← ثبت سفارش (پرداخت‌شده) ← نمایش در پنل مدیریت.

### موجودی نامحدود محصولات

در ویرایش محصول، گزینه «موجودی نامحدود (بدون محدودیت تعداد)» به‌صورت پیش‌فرض
فعال است. در این حالت فقط ترکیب‌های رنگ و سایز قابل سفارش را تیک بزنید؛ تعداد
موجودی وارد نمی‌شود و خرید از موجودی کم نمی‌کند. با برداشتن تیک، تعداد هر ترکیب
نمایش داده می‌شود و محدودیت موجودی در سبد و تسویه اعمال می‌شود. تغییر حالت،
تعدادهای ذخیره‌شده را پاک نمی‌کند. غیرفعال کردن یک ترکیب، خرید آن را متوقف می‌کند
و سابقه سفارش‌های قبلی را نگه می‌دارد.

مایگریشن `AddUnlimitedProductStock` موجودی نامحدود را برای محصولات موجود هم فعال
می‌کند. برای محصولاتی که باید محدود بمانند، گزینه را غیرفعال و تعدادها را بررسی کنید.
آزمون موجودی: `dotnet run --project tests/InventoryChecks/InventoryChecks.csproj`

### مالیات سفارش‌ها

در پنل مدیریت ← «تنظیمات فروشگاه» (`/admin/settings`) می‌توان مالیات ۱۰٪ را
برای تمام سفارش‌های جدید فعال یا غیرفعال کرد. پیش‌فرض غیرفعال است. مالیات روی
جمع کالاها محاسبه و به نزدیک‌ترین تومان گرد می‌شود؛ هزینه ارسال مشمول نیست.
وضعیت، نرخ و مبلغ مالیات همراه هر سفارش ذخیره می‌شود؛ تغییر تنظیم، سفارش‌ها و
فاکتورهای قبلی را تغییر نمی‌دهد. سبد خرید، تسویه، سوابق، فاکتورها و رسید چاپی
وضعیت و مبلغ را نمایش می‌دهند. تغییر تنظیم هنگام تسویه نیازمند بررسی مجدد مبلغ است.

مایگریشن `StoreTaxSettingsAndOrderSnapshots` سفارش‌های قدیمی را بدون مالیات
علامت می‌زند و مبلغ نهایی آن‌ها را تغییر نمی‌دهد. آزمون `tests/TaxChecks` فقط با
پایگاه داده دورریختنی با نامی که با `papey_tax_review_` شروع شود اجرا می‌شود؛
متغیرهای اتصال استاندارد پروژه را برای همان پایگاه تنظیم کنید، سپس اجرا کنید:
`dotnet run --project tests/TaxChecks -c Release`.

### حذف نوع لباس

در مدیریت انواع محصول، حذف فقط برای نوعی بدون محصول مجاز است (محصول غیرفعال
هم مانع حذف است). بررسی و حذف در تراکنش با قفل سطر انجام می‌شود. دسته‌بندی‌ها
حفظ و از نوع حذف‌شده جدا می‌شوند؛ راه‌اندازی مجدد، نوع حذف‌شده را بازسازی نمی‌کند.
آزمون `dotnet run --project tests/ProductTypeChecks -c Release` فقط روی پایگاه
داده دورریختنی جدید با پیشوند `papey_type_review_` و متغیرهای اتصال آن اجرا شود.

### ورود مدیر با شماره موبایل

شماره‌های `ADMIN_PHONES` به همان مدیر موجود با نام `ADMIN_USERNAME` متصل هستند.
ثبت‌نام حساب جداگانه با این شماره‌ها یا نام کاربری برابر آن‌ها مجاز نیست؛ قالب‌های
ایرانی، بین‌المللی و ارقام فارسی پیش از بررسی یکسان‌سازی می‌شوند. ورود موفق مدیر
همیشه به `/admin` می‌رود، حتی با `returnUrl` مربوط به خرید. آزمون
`dotnet run --project tests/AdminAuthChecks -c Release` فقط با پایگاه دورریختنی
جدید با پیشوند `papey_admin_review_` اجرا شود؛ پیامکی ارسال نمی‌کند.
