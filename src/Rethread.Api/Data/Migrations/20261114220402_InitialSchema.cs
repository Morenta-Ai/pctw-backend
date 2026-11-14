using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Rethread.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "collector_number_seq",
                startValue: 542L);

            migrationBuilder.CreateSequence(
                name: "order_number_seq",
                startValue: 5135L);

            migrationBuilder.CreateSequence(
                name: "pickup_number_seq",
                startValue: 24850L);

            migrationBuilder.CreateSequence(
                name: "waybill_number_seq",
                startValue: 100L);

            migrationBuilder.CreateTable(
                name: "customers",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    first_name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    last_name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    mobile = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    language = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status_reason = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "data_protection_keys",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    friendly_name = table.Column<string>(type: "text", nullable: true),
                    xml = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_protection_keys", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "donation_projects",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    summary = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    goal_cents = table.Column<long>(type: "bigint", nullable: false),
                    raised_cents = table.Column<long>(type: "bigint", nullable: false),
                    donor_count = table.Column<int>(type: "integer", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    ends_on = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_donation_projects", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "files",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    content_type = table.Column<string>(type: "text", nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    path = table.Column<string>(type: "text", nullable: false),
                    uploaded_by = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_files", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "garment_types",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    estimated_kg = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_garment_types", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "hub_deliveries",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    hub_id = table.Column<string>(type: "text", nullable: false),
                    collector_id = table.Column<string>(type: "text", nullable: false),
                    job_count = table.Column<int>(type: "integer", nullable: false),
                    weight_kg = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hub_deliveries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "hubs",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    closes_at = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    latitude = table.Column<double>(type: "double precision", nullable: false),
                    longitude = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hubs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "impact_multipliers",
                columns: table => new
                {
                    key = table.Column<string>(type: "text", nullable: false),
                    unit = table.Column<string>(type: "text", nullable: false),
                    per_kg = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_impact_multipliers", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "notifications",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    audience = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    recipient_id = table.Column<string>(type: "text", nullable: true),
                    type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    target_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    target_id = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    read_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notifications", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "otp_challenges",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    audience = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    channel = table.Column<string>(type: "text", nullable: false),
                    destination = table.Column<string>(type: "text", nullable: false),
                    subject_id = table.Column<string>(type: "text", nullable: true),
                    code_hash = table.Column<string>(type: "text", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    resend_after = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    consumed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_otp_challenges", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "pickup_photos",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    pickup_id = table.Column<string>(type: "text", nullable: false),
                    collector_id = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    file_id = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pickup_photos", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "products",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    price_cents = table.Column<long>(type: "bigint", nullable: false),
                    sizes = table.Column<List<string>>(type: "text[]", nullable: false),
                    stock = table.Column<int>(type: "integer", nullable: false),
                    published = table.Column<bool>(type: "boolean", nullable: false),
                    story = table.Column<string>(type: "text", nullable: false),
                    image_urls = table.Column<List<string>>(type: "text[]", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_products", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "rate_sets",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    customer_rate_per_kg_cents = table.Column<long>(type: "bigint", nullable: false),
                    collector_rate_per_kg_cents = table.Column<long>(type: "bigint", nullable: false),
                    home_delivery_charge_cents = table.Column<long>(type: "bigint", nullable: false),
                    click_and_collect_charge_cents = table.Column<long>(type: "bigint", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rate_sets", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    audience = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    subject_id = table.Column<string>(type: "text", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    replaced_by_id = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refresh_tokens", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "staff",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: true),
                    invite_token_hash = table.Column<string>(type: "text", nullable: true),
                    invite_expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_sign_in_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    notifications_read_before = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_staff", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "vendors",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    address = table.Column<string>(type: "text", nullable: false),
                    latitude = table.Column<double>(type: "double precision", nullable: false),
                    longitude = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vendors", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "voucher_brands",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    code_prefix = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    values_cents = table.Column<List<long>>(type: "bigint[]", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_voucher_brands", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "wallet_transactions",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    customer_id = table.Column<string>(type: "text", nullable: false),
                    type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    amount_cents = table.Column<long>(type: "bigint", nullable: false),
                    occurred_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    pickup_id = table.Column<string>(type: "text", nullable: true),
                    weight_kg = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    rate_per_kg_cents = table.Column<long>(type: "bigint", nullable: true),
                    redemption_id = table.Column<string>(type: "text", nullable: true),
                    order_id = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wallet_transactions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "addresses",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    customer_id = table.Column<string>(type: "character varying(40)", nullable: false),
                    label = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    line = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    area = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    latitude = table.Column<double>(type: "double precision", nullable: false),
                    longitude = table.Column<double>(type: "double precision", nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_addresses", x => x.id);
                    table.ForeignKey(
                        name: "fk_addresses_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bank_details",
                columns: table => new
                {
                    customer_id = table.Column<string>(type: "character varying(40)", nullable: false),
                    bank_name = table.Column<string>(type: "text", nullable: false),
                    branch = table.Column<string>(type: "text", nullable: false),
                    account_name = table.Column<string>(type: "text", nullable: false),
                    account_number_protected = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    account_last4 = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bank_details", x => x.customer_id);
                    table.ForeignKey(
                        name: "fk_bank_details_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "family_members",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    customer_id = table.Column<string>(type: "character varying(40)", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    relation = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    gender = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    age_group = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    height_cm = table.Column<decimal>(type: "numeric(5,1)", precision: 5, scale: 1, nullable: true),
                    chest_cm = table.Column<decimal>(type: "numeric(5,1)", precision: 5, scale: 1, nullable: true),
                    waist_cm = table.Column<decimal>(type: "numeric(5,1)", precision: 5, scale: 1, nullable: true),
                    hip_cm = table.Column<decimal>(type: "numeric(5,1)", precision: 5, scale: 1, nullable: true),
                    shoe_size = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_family_members", x => x.id);
                    table.ForeignKey(
                        name: "fk_family_members_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "redemptions",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    customer_id = table.Column<string>(type: "character varying(40)", nullable: false),
                    type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    amount_cents = table.Column<long>(type: "bigint", nullable: false),
                    bank_name = table.Column<string>(type: "text", nullable: true),
                    branch = table.Column<string>(type: "text", nullable: true),
                    account_name = table.Column<string>(type: "text", nullable: true),
                    account_number_protected = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    voucher_brand_id = table.Column<string>(type: "text", nullable: true),
                    voucher_brand_name = table.Column<string>(type: "text", nullable: true),
                    voucher_code = table.Column<string>(type: "text", nullable: true),
                    project_id = table.Column<string>(type: "text", nullable: true),
                    requested_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    processed_by = table.Column<string>(type: "text", nullable: true),
                    reference = table.Column<string>(type: "text", nullable: true),
                    reject_reason = table.Column<string>(type: "text", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_redemptions", x => x.id);
                    table.ForeignKey(
                        name: "fk_redemptions_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_redemptions_donation_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "donation_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "collectors",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    code = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    first_name = table.Column<string>(type: "text", nullable: false),
                    last_name = table.Column<string>(type: "text", nullable: false),
                    mobile = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status_reason = table.Column<string>(type: "text", nullable: true),
                    online = table.Column<bool>(type: "boolean", nullable: false),
                    latitude = table.Column<double>(type: "double precision", nullable: true),
                    longitude = table.Column<double>(type: "double precision", nullable: true),
                    last_seen_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    radius_km = table.Column<int>(type: "integer", nullable: false),
                    vehicle_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    vehicle_plate = table.Column<string>(type: "text", nullable: false),
                    capacity_kg = table.Column<int>(type: "integer", nullable: false),
                    area = table.Column<string>(type: "text", nullable: false),
                    region = table.Column<string>(type: "text", nullable: false),
                    suburbs = table.Column<List<string>>(type: "text[]", nullable: false),
                    hub_id = table.Column<string>(type: "text", nullable: false),
                    scale_name = table.Column<string>(type: "text", nullable: true),
                    scale_paired = table.Column<bool>(type: "boolean", nullable: false),
                    scale_auto_connect = table.Column<bool>(type: "boolean", nullable: false),
                    language = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    rating = table.Column<decimal>(type: "numeric(2,1)", precision: 2, scale: 1, nullable: false),
                    member_since = table.Column<DateOnly>(type: "date", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_collectors", x => x.id);
                    table.ForeignKey(
                        name: "fk_collectors_hubs_hub_id",
                        column: x => x.hub_id,
                        principalTable: "hubs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "staff_notification_reads",
                columns: table => new
                {
                    notification_id = table.Column<string>(type: "character varying(80)", nullable: false),
                    staff_id = table.Column<string>(type: "character varying(40)", nullable: false),
                    read_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_staff_notification_reads", x => new { x.staff_id, x.notification_id });
                    table.ForeignKey(
                        name: "fk_staff_notification_reads_notifications_notification_id",
                        column: x => x.notification_id,
                        principalTable: "notifications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_staff_notification_reads_staff_staff_id",
                        column: x => x.staff_id,
                        principalTable: "staff",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "orders",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    customer_id = table.Column<string>(type: "character varying(40)", nullable: false),
                    fulfilment_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    destination = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    address_id = table.Column<string>(type: "text", nullable: true),
                    vendor_id = table.Column<string>(type: "text", nullable: true),
                    payment_method = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    payment_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    subtotal_cents = table.Column<long>(type: "bigint", nullable: false),
                    transport_charge_cents = table.Column<long>(type: "bigint", nullable: false),
                    total_cents = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    waybill_number = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    estimated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_orders", x => x.id);
                    table.ForeignKey(
                        name: "fk_orders_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_orders_vendors_vendor_id",
                        column: x => x.vendor_id,
                        principalTable: "vendors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pickups",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    customer_id = table.Column<string>(type: "character varying(40)", nullable: false),
                    collector_id = table.Column<string>(type: "character varying(40)", nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    method = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    bulk_mode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    declared_weight_kg = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    declared_pieces = table.Column<int>(type: "integer", nullable: true),
                    estimated_kg = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    item_count = table.Column<int>(type: "integer", nullable: true),
                    bag_count = table.Column<int>(type: "integer", nullable: false),
                    actual_weight_kg = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    weight_source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    scheduled_date = table.Column<DateOnly>(type: "date", nullable: false),
                    slot = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    window_start = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    window_end = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    address_id = table.Column<string>(type: "text", nullable: true),
                    address_line = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    area = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    latitude = table.Column<double>(type: "double precision", nullable: false),
                    longitude = table.Column<double>(type: "double precision", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    customer_rate_per_kg_cents = table.Column<long>(type: "bigint", nullable: false),
                    collector_rate_per_kg_cents = table.Column<long>(type: "bigint", nullable: false),
                    customer_payout_cents = table.Column<long>(type: "bigint", nullable: true),
                    collector_payout_cents = table.Column<long>(type: "bigint", nullable: true),
                    hub_id = table.Column<string>(type: "text", nullable: true),
                    hub_delivery_id = table.Column<string>(type: "text", nullable: true),
                    cancel_reason = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    collected_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    delivered_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pickups", x => x.id);
                    table.ForeignKey(
                        name: "fk_pickups_collectors_collector_id",
                        column: x => x.collector_id,
                        principalTable: "collectors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pickups_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pickups_hubs_hub_id",
                        column: x => x.hub_id,
                        principalTable: "hubs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "order_events",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    order_id = table.Column<string>(type: "character varying(20)", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    occurred_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    actor = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_order_events_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "order_items",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    order_id = table.Column<string>(type: "character varying(20)", nullable: false),
                    product_id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    size = table.Column<string>(type: "text", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    unit_price_cents = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_order_items_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "assignments",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    pickup_id = table.Column<string>(type: "character varying(20)", nullable: false),
                    collector_id = table.Column<string>(type: "character varying(40)", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    offered_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    responded_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    decline_reason = table.Column<string>(type: "text", nullable: true),
                    offered_by_staff_id = table.Column<string>(type: "text", nullable: true),
                    offered_by = table.Column<string>(type: "text", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assignments", x => x.id);
                    table.ForeignKey(
                        name: "fk_assignments_collectors_collector_id",
                        column: x => x.collector_id,
                        principalTable: "collectors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_assignments_pickups_pickup_id",
                        column: x => x.pickup_id,
                        principalTable: "pickups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pickup_events",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    pickup_id = table.Column<string>(type: "character varying(20)", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    occurred_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    actor = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pickup_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_pickup_events_pickups_pickup_id",
                        column: x => x.pickup_id,
                        principalTable: "pickups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pickup_items",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    pickup_id = table.Column<string>(type: "character varying(20)", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    garment_type = table.Column<string>(type: "text", nullable: true),
                    member_id = table.Column<string>(type: "text", nullable: true),
                    size = table.Column<string>(type: "text", nullable: true),
                    quantity = table.Column<int>(type: "integer", nullable: true),
                    bag_label = table.Column<string>(type: "text", nullable: true),
                    condition = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pickup_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_pickup_items_pickups_pickup_id",
                        column: x => x.pickup_id,
                        principalTable: "pickups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_addresses_one_default",
                table: "addresses",
                column: "customer_id",
                unique: true,
                filter: "is_default");

            migrationBuilder.CreateIndex(
                name: "ix_assignments_collector_id_status",
                table: "assignments",
                columns: new[] { "collector_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_assignments_one_open_offer",
                table: "assignments",
                column: "pickup_id",
                unique: true,
                filter: "status = 'offered'");

            migrationBuilder.CreateIndex(
                name: "ix_assignments_status_expires_at",
                table: "assignments",
                columns: new[] { "status", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_collectors_code",
                table: "collectors",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_collectors_hub_id",
                table: "collectors",
                column: "hub_id");

            migrationBuilder.CreateIndex(
                name: "ix_collectors_mobile",
                table: "collectors",
                column: "mobile",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_customers_created_at",
                table: "customers",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_customers_email",
                table: "customers",
                column: "email",
                unique: true,
                filter: "email IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_customers_mobile",
                table: "customers",
                column: "mobile",
                unique: true,
                filter: "mobile IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_family_members_customer_id",
                table: "family_members",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_hub_deliveries_collector_id_created_at",
                table: "hub_deliveries",
                columns: new[] { "collector_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_hubs_code",
                table: "hubs",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_notifications_audience_recipient_id_created_at",
                table: "notifications",
                columns: new[] { "audience", "recipient_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_order_events_order_id",
                table: "order_events",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_order_items_order_id",
                table: "order_items",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_orders_customer_id_created_at",
                table: "orders",
                columns: new[] { "customer_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_orders_status_created_at",
                table: "orders",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_orders_vendor_id",
                table: "orders",
                column: "vendor_id");

            migrationBuilder.CreateIndex(
                name: "ix_orders_waybill_number",
                table: "orders",
                column: "waybill_number",
                unique: true,
                filter: "waybill_number IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_otp_challenges_audience_destination_created_at",
                table: "otp_challenges",
                columns: new[] { "audience", "destination", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_pickup_events_pickup_id_occurred_at",
                table: "pickup_events",
                columns: new[] { "pickup_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_pickup_events_status_occurred_at",
                table: "pickup_events",
                columns: new[] { "status", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_pickup_items_pickup_id",
                table: "pickup_items",
                column: "pickup_id");

            migrationBuilder.CreateIndex(
                name: "ix_pickup_photos_pickup_id",
                table: "pickup_photos",
                column: "pickup_id");

            migrationBuilder.CreateIndex(
                name: "ix_pickups_collector_id_status",
                table: "pickups",
                columns: new[] { "collector_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_pickups_created_at",
                table: "pickups",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_pickups_customer_id_created_at",
                table: "pickups",
                columns: new[] { "customer_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_pickups_hub_id",
                table: "pickups",
                column: "hub_id");

            migrationBuilder.CreateIndex(
                name: "ix_pickups_status_scheduled_date",
                table: "pickups",
                columns: new[] { "status", "scheduled_date" });

            migrationBuilder.CreateIndex(
                name: "ix_products_created_at",
                table: "products",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_rate_sets_effective_from",
                table: "rate_sets",
                column: "effective_from",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_redemptions_customer_id",
                table: "redemptions",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_redemptions_project_id",
                table: "redemptions",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_redemptions_type_status_requested_at",
                table: "redemptions",
                columns: new[] { "type", "status", "requested_at" });

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_audience_subject_id",
                table: "refresh_tokens",
                columns: new[] { "audience", "subject_id" });

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_token_hash",
                table: "refresh_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_staff_email",
                table: "staff",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_staff_invite_token_hash",
                table: "staff",
                column: "invite_token_hash",
                unique: true,
                filter: "invite_token_hash IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_staff_notification_reads_notification_id",
                table: "staff_notification_reads",
                column: "notification_id");

            migrationBuilder.CreateIndex(
                name: "ix_wallet_transactions_customer_id_occurred_at",
                table: "wallet_transactions",
                columns: new[] { "customer_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_wallet_transactions_redemption_id",
                table: "wallet_transactions",
                column: "redemption_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "addresses");

            migrationBuilder.DropTable(
                name: "assignments");

            migrationBuilder.DropTable(
                name: "bank_details");

            migrationBuilder.DropTable(
                name: "data_protection_keys");

            migrationBuilder.DropTable(
                name: "family_members");

            migrationBuilder.DropTable(
                name: "files");

            migrationBuilder.DropTable(
                name: "garment_types");

            migrationBuilder.DropTable(
                name: "hub_deliveries");

            migrationBuilder.DropTable(
                name: "impact_multipliers");

            migrationBuilder.DropTable(
                name: "order_events");

            migrationBuilder.DropTable(
                name: "order_items");

            migrationBuilder.DropTable(
                name: "otp_challenges");

            migrationBuilder.DropTable(
                name: "pickup_events");

            migrationBuilder.DropTable(
                name: "pickup_items");

            migrationBuilder.DropTable(
                name: "pickup_photos");

            migrationBuilder.DropTable(
                name: "products");

            migrationBuilder.DropTable(
                name: "rate_sets");

            migrationBuilder.DropTable(
                name: "redemptions");

            migrationBuilder.DropTable(
                name: "refresh_tokens");

            migrationBuilder.DropTable(
                name: "staff_notification_reads");

            migrationBuilder.DropTable(
                name: "voucher_brands");

            migrationBuilder.DropTable(
                name: "wallet_transactions");

            migrationBuilder.DropTable(
                name: "orders");

            migrationBuilder.DropTable(
                name: "pickups");

            migrationBuilder.DropTable(
                name: "donation_projects");

            migrationBuilder.DropTable(
                name: "notifications");

            migrationBuilder.DropTable(
                name: "staff");

            migrationBuilder.DropTable(
                name: "vendors");

            migrationBuilder.DropTable(
                name: "collectors");

            migrationBuilder.DropTable(
                name: "customers");

            migrationBuilder.DropTable(
                name: "hubs");

            migrationBuilder.DropSequence(
                name: "collector_number_seq");

            migrationBuilder.DropSequence(
                name: "order_number_seq");

            migrationBuilder.DropSequence(
                name: "pickup_number_seq");

            migrationBuilder.DropSequence(
                name: "waybill_number_seq");
        }
    }
}
