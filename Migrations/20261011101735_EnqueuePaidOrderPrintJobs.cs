using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Papey.Migrations
{
    /// <inheritdoc />
    public partial class EnqueuePaidOrderPrintJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Runs in the payment transaction, including updates from any app instance.
            // Existing paid orders are not backfilled, and repeat callbacks cannot enqueue twice.
            migrationBuilder.Sql("""
                CREATE FUNCTION papey_enqueue_paid_order_print_job() RETURNS trigger
                LANGUAGE plpgsql AS $function$
                BEGIN
                    IF NEW."Status" = 1 AND NEW."PaidAt" IS NOT NULL
                        AND NULLIF(NEW."PaymentRefId", '') IS NOT NULL THEN
                        IF TG_OP = 'INSERT' OR OLD."PaidAt" IS NULL THEN
                            INSERT INTO "OrderPrintJobs" ("OrderId", "Attempts")
                            VALUES (NEW."Id", 0)
                            ON CONFLICT ("OrderId") DO NOTHING;
                        END IF;
                    END IF;
                    RETURN NEW;
                END;
                $function$;

                CREATE TRIGGER "EnqueuePaidOrderPrintJob"
                AFTER INSERT OR UPDATE OF "Status", "PaidAt", "PaymentRefId" ON "Orders"
                FOR EACH ROW EXECUTE FUNCTION papey_enqueue_paid_order_print_job();
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS "EnqueuePaidOrderPrintJob" ON "Orders";
                DROP FUNCTION IF EXISTS papey_enqueue_paid_order_print_job();
                """);

        }
    }
}
