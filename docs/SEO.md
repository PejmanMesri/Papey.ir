# Papey SEO

## Search-intent map
- Home: تیشرت چاپی and brand پاپی / Papey.
- /printed-tshirts: shopping for تیشرت چاپی; real catalog links plus size, shipping and custom-order information.
- /printed-hoodies: هودی چاپی، خرید هودی چاپی، سفارش هودی چاپی، هودی کاستوم and printing on hoodies. Real hoodie catalog and model-specific placement guidance.
- /custom-design: سفارش تیشرت کاستوم; phone callback and WhatsApp, no promise of immediate production or free garments.
- /guides/tshirt-print-design: طراحی تیشرت چاپی; original Persian file-preparation guide linked to catalog and custom ordering.
- Product/type/category URLs: specific product or collection intent; no invented descriptions or reviews.

## Technical behavior
Public metadata and JSON-LD are emitted in initial HTML. Canonicals use configured SITE_URL, never request Host. Query tracking is omitted; invalid types, empty categories and faceted sorting are noindex. Missing product/category URLs return actual HTTP 404. /contact redirects permanently to /about#contact. Public trailing-slash duplicates redirect permanently.

The dynamic sitemap includes active catalog URLs, landing/guide pages and real product images. It excludes account/admin/search and inactive products. No synthetic lastmod dates. Product offers use the displayed product/variant price after the active store-wide discount, in IRR (stored toman × 10). Vouchers remain conditional checkout incentives and are not advertised as unconditional product prices. No fabricated ratings, stock, address, return policy or delivery promises.

## Search Console handoff
Browser access failed during this task; no submission or private indexing inspection is claimed. In the verified papey.ir domain property, submit https://papey.ir/sitemap.xml. Inspect the homepage, printed-tshirts, custom-design and design-guide canonical URLs, run live test and request indexing if eligible. Review Pages, Crawl stats and Product snippets reports after Google recrawls. Track clicks/impressions for target phrases; indexing and ranking are decided by Google and are not guaranteed by a deployment.

## Validation
Run tests/SeoChecks with PAPEY_TEST_CONNECTION pointed at a disposable PostgreSQL instance. It creates and removes its own test database. Includes currency conversion, stock state, escaping, canonicals, image sitemap, search-intent titles, noindex filters, rendered metadata, real 404s and redirects.

Official guidance:
- https://developers.google.com/search/docs/specialty/ecommerce
- https://developers.google.com/search/docs/appearance/title-link
- https://developers.google.com/search/docs/specialty/ecommerce/designing-a-url-structure-for-ecommerce-sites
- https://developers.google.com/search/docs/appearance/structured-data/product-snippet

## Expanded Persian phrase coverage
Related phrases appear in useful visible content, headings and links rather than hidden keyword lists or duplicate landing pages: سفارش تیشرت چاپی، خرید تیشرت چاپی، سفارش لباس چاپی، چاپ روی لباس، تیشرت سفارشی، چاپ طرح دلخواه روی تیشرت، هودی با طرح دلخواه، چاپ روی هودی، پوشاک چاپی و سفارش گروهی. Exact product availability remains dynamic; do not claim printing methods, materials or turnaround beyond actual shop information.

## Dynamic clothing types and DTF
The owner confirmed that Papey uses DTF (not PDF). /guides/dtf-printing explains the method, تیشرت DTF، لباس DTF and چاپ دی‌تی‌اف, linking to shopping and custom-order pages. Definition checked against Epson: https://www.epson.co.uk/en_GB/buying-guides/guide-to-printing-on-garments . PDF is described only as a file format, not an offered printing method.

New active clothing types with active products automatically receive collection metadata, a sitemap entry and a homepage search link. Names are derived from admin data, including future کاپشن، پلیور and other types; no hardcoded list or new SEO deployment required. Basic/plain labels remain plain. Empty/deactivated types are excluded from type-page indexing and sitemap coverage. Homepage discovery refreshes on page load. No unsupported future stock or printing compatibility is advertised in advance.
