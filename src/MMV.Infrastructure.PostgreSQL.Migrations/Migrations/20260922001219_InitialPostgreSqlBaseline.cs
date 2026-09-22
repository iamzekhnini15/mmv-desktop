using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace MMV.Infrastructure.PostgreSQL.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class InitialPostgreSqlBaseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Customers",
                columns: table => new
                {
                    CustomerId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FirstName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    BirthDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    Address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    City = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PostalCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    SocialSecurityNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    InsuranceName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customers", x => x.CustomerId);
                });

            migrationBuilder.CreateTable(
                name: "DocumentSequences",
                columns: table => new
                {
                    SequenceName = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Prefix = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    CurrentValue = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentSequences", x => x.SequenceName);
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    NotificationId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    EntityId = table.Column<long>(type: "bigint", nullable: true),
                    EntityType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    IsRead = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.NotificationId);
                });

            migrationBuilder.CreateTable(
                name: "ProductCategories",
                columns: table => new
                {
                    CategoryId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductCategories", x => x.CategoryId);
                });

            migrationBuilder.CreateTable(
                name: "Supplements",
                columns: table => new
                {
                    SupplementId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SupplementPrice = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Supplements", x => x.SupplementId);
                });

            migrationBuilder.CreateTable(
                name: "Suppliers",
                columns: table => new
                {
                    SupplierId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ContactEmail = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    Phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ReferenceCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Suppliers", x => x.SupplierId);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    UserId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Username = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    NormalizedUsername = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: false),
                    FirstName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Role = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    LastLogin = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.UserId);
                });

            migrationBuilder.CreateTable(
                name: "Prescriptions",
                columns: table => new
                {
                    PrescriptionId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CustomerId = table.Column<long>(type: "bigint", nullable: false),
                    IssueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DoctorName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    OdSphere = table.Column<double>(type: "double precision", nullable: true),
                    OdCylinder = table.Column<double>(type: "double precision", nullable: true),
                    OdAxis = table.Column<int>(type: "integer", nullable: true),
                    OdAddition = table.Column<double>(type: "double precision", nullable: true),
                    OdPrismValue = table.Column<double>(type: "double precision", nullable: true),
                    OdPrismBase = table.Column<string>(type: "text", nullable: true),
                    OdVisualAcuity = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    OgSphere = table.Column<double>(type: "double precision", nullable: true),
                    OgCylinder = table.Column<double>(type: "double precision", nullable: true),
                    OgAxis = table.Column<int>(type: "integer", nullable: true),
                    OgAddition = table.Column<double>(type: "double precision", nullable: true),
                    OgPrismValue = table.Column<double>(type: "double precision", nullable: true),
                    OgPrismBase = table.Column<string>(type: "text", nullable: true),
                    OgVisualAcuity = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Prescriptions", x => x.PrescriptionId);
                    table.ForeignKey(
                        name: "FK_Prescriptions_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "CustomerId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Products",
                columns: table => new
                {
                    ProductId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Reference = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    NormalizedReference = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Category = table.Column<string>(type: "text", nullable: false),
                    CategoryId = table.Column<long>(type: "bigint", nullable: true),
                    SupplierId = table.Column<long>(type: "bigint", nullable: false),
                    PurchasePrice = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    SalePrice = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    RecommendedPrice = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    StockQuantity = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    StockAlertThreshold = table.Column<int>(type: "integer", nullable: false, defaultValue: 5),
                    TechnicalSpecs = table.Column<string>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    EntryDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Products", x => x.ProductId);
                    table.ForeignKey(
                        name: "FK_Products_ProductCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "ProductCategories",
                        principalColumn: "CategoryId",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Products_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "SupplierId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Sales",
                columns: table => new
                {
                    SaleId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SaleNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CustomerId = table.Column<long>(type: "bigint", nullable: true),
                    StaffId = table.Column<long>(type: "bigint", nullable: true),
                    SaleDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EstimatedDelivery = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TotalAmount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false, defaultValue: 0m),
                    FinalAmount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    DepositAmount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    RemainingAmount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    PaymentMethod = table.Column<string>(type: "text", nullable: false),
                    PaymentStatus = table.Column<string>(type: "text", nullable: false, defaultValue: "Paid"),
                    Status = table.Column<string>(type: "text", nullable: false, defaultValue: "Draft"),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sales", x => x.SaleId);
                    table.ForeignKey(
                        name: "FK_Sales_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "CustomerId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Sales_Users_StaffId",
                        column: x => x.StaffId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "AccessoryDetails",
                columns: table => new
                {
                    ProductId = table.Column<long>(type: "bigint", nullable: false),
                    Color = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Size = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Material = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessoryDetails", x => x.ProductId);
                    table.ForeignKey(
                        name: "FK_AccessoryDetails_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "ProductId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GlassDetails",
                columns: table => new
                {
                    ProductId = table.Column<long>(type: "bigint", nullable: false),
                    Material = table.Column<string>(type: "text", nullable: true),
                    GlassType = table.Column<string>(type: "text", nullable: true),
                    Diameter = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Index = table.Column<decimal>(type: "numeric(3,2)", precision: 3, scale: 2, nullable: true),
                    PowerLimitMin = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    PowerLimitMax = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GlassDetails", x => x.ProductId);
                    table.ForeignKey(
                        name: "FK_GlassDetails_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "ProductId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LensDetails",
                columns: table => new
                {
                    ProductId = table.Column<long>(type: "bigint", nullable: false),
                    Brand = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Model = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Material = table.Column<string>(type: "text", nullable: true),
                    LensType = table.Column<string>(type: "text", nullable: true),
                    Diameter = table.Column<decimal>(type: "numeric(4,2)", precision: 4, scale: 2, nullable: true),
                    BaseCurve = table.Column<decimal>(type: "numeric(4,2)", precision: 4, scale: 2, nullable: true),
                    IsColored = table.Column<bool>(type: "boolean", nullable: false),
                    Duration = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LensDetails", x => x.ProductId);
                    table.ForeignKey(
                        name: "FK_LensDetails_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "ProductId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StockMovements",
                columns: table => new
                {
                    MovementId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProductId = table.Column<long>(type: "bigint", nullable: false),
                    MovementType = table.Column<string>(type: "text", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PerformedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockMovements", x => x.MovementId);
                    table.ForeignKey(
                        name: "FK_StockMovements_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "ProductId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockMovements_Users_PerformedByUserId",
                        column: x => x.PerformedByUserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Orders",
                columns: table => new
                {
                    OrderId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrderNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SaleId = table.Column<long>(type: "bigint", nullable: false),
                    SupplierId = table.Column<long>(type: "bigint", nullable: true),
                    OrderDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EstimatedDelivery = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReceivedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false, defaultValue: "New"),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orders", x => x.OrderId);
                    table.ForeignKey(
                        name: "FK_Orders_Sales_SaleId",
                        column: x => x.SaleId,
                        principalTable: "Sales",
                        principalColumn: "SaleId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SaleItems",
                columns: table => new
                {
                    SaleItemId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SaleId = table.Column<long>(type: "bigint", nullable: false),
                    ProductId = table.Column<long>(type: "bigint", nullable: true),
                    ItemType = table.Column<string>(type: "text", nullable: false, defaultValue: "Frame"),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    TotalPrice = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    UsageType = table.Column<string>(type: "text", nullable: true),
                    Sphere = table.Column<double>(type: "double precision", nullable: true),
                    Cylinder = table.Column<double>(type: "double precision", nullable: true),
                    Axis = table.Column<int>(type: "integer", nullable: true),
                    Addition = table.Column<double>(type: "double precision", nullable: true),
                    PrismValue = table.Column<double>(type: "double precision", nullable: true),
                    PrismBase = table.Column<string>(type: "text", nullable: true),
                    VisualAcuity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaleItems", x => x.SaleItemId);
                    table.ForeignKey(
                        name: "FK_SaleItems_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "ProductId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SaleItems_Sales_SaleId",
                        column: x => x.SaleId,
                        principalTable: "Sales",
                        principalColumn: "SaleId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GlassPricingTiers",
                columns: table => new
                {
                    TierId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GlassId = table.Column<long>(type: "bigint", nullable: false),
                    PowerMin = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    PowerMax = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    PurchasePriceGrid = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    SalePriceGrid = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GlassPricingTiers", x => x.TierId);
                    table.ForeignKey(
                        name: "FK_GlassPricingTiers_GlassDetails_GlassId",
                        column: x => x.GlassId,
                        principalTable: "GlassDetails",
                        principalColumn: "ProductId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GlassSupplements",
                columns: table => new
                {
                    GlassId = table.Column<long>(type: "bigint", nullable: false),
                    SupplementId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GlassSupplements", x => new { x.GlassId, x.SupplementId });
                    table.ForeignKey(
                        name: "FK_GlassSupplements_GlassDetails_GlassId",
                        column: x => x.GlassId,
                        principalTable: "GlassDetails",
                        principalColumn: "ProductId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GlassSupplements_Supplements_SupplementId",
                        column: x => x.SupplementId,
                        principalTable: "Supplements",
                        principalColumn: "SupplementId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrderItems",
                columns: table => new
                {
                    OrderItemId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrderId = table.Column<long>(type: "bigint", nullable: false),
                    ProductId = table.Column<long>(type: "bigint", nullable: true),
                    ItemType = table.Column<string>(type: "text", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    UnitPrice = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    UsageType = table.Column<string>(type: "text", nullable: true),
                    Sphere = table.Column<double>(type: "double precision", nullable: true),
                    Cylinder = table.Column<double>(type: "double precision", nullable: true),
                    Axis = table.Column<int>(type: "integer", nullable: true),
                    Addition = table.Column<double>(type: "double precision", nullable: true),
                    PrismValue = table.Column<double>(type: "double precision", nullable: true),
                    PrismBase = table.Column<string>(type: "text", nullable: true),
                    VisualAcuity = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderItems", x => x.OrderItemId);
                    table.ForeignKey(
                        name: "FK_OrderItems_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "OrderId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrderItems_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "ProductId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkshopSheets",
                columns: table => new
                {
                    WorkshopSheetId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrderId = table.Column<long>(type: "bigint", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    IsCurrent = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TechnicalFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OrderNumberSnapshot = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    OrderDateSnapshot = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EstimatedDeliverySnapshot = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CustomerNameSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    InstructionsSnapshot = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    QcStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    QcComment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    QcCompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkshopSheets", x => x.WorkshopSheetId);
                    table.ForeignKey(
                        name: "FK_WorkshopSheets_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "OrderId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkshopSheetItems",
                columns: table => new
                {
                    WorkshopSheetItemId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WorkshopSheetId = table.Column<long>(type: "bigint", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    ItemType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SourceProductId = table.Column<long>(type: "bigint", nullable: true),
                    ProductReferenceSnapshot = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ProductNameSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ProductCategorySnapshot = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    UsageType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    SourceSphere = table.Column<double>(type: "double precision", nullable: true),
                    SourceCylinder = table.Column<double>(type: "double precision", nullable: true),
                    SourceAxis = table.Column<int>(type: "integer", nullable: true),
                    Addition = table.Column<double>(type: "double precision", nullable: true),
                    PrismValue = table.Column<double>(type: "double precision", nullable: true),
                    PrismBase = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    VisualAcuity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    TransposedSphere = table.Column<double>(type: "double precision", nullable: true),
                    TransposedCylinder = table.Column<double>(type: "double precision", nullable: true),
                    TransposedAxis = table.Column<int>(type: "integer", nullable: true),
                    HasTransposition = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkshopSheetItems", x => x.WorkshopSheetItemId);
                    table.ForeignKey(
                        name: "FK_WorkshopSheetItems_WorkshopSheets_WorkshopSheetId",
                        column: x => x.WorkshopSheetId,
                        principalTable: "WorkshopSheets",
                        principalColumn: "WorkshopSheetId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "DocumentSequences",
                columns: new[] { "SequenceName", "CurrentValue", "Prefix", "UpdatedAt" },
                values: new object[,]
                {
                    { "ORDER", 0L, "CMD", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "SALE", 0L, "VTE", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) }
                });

            migrationBuilder.CreateIndex(
                name: "idx_customers_lastname",
                table: "Customers",
                column: "LastName");

            migrationBuilder.CreateIndex(
                name: "idx_customers_phone",
                table: "Customers",
                column: "Phone");

            migrationBuilder.CreateIndex(
                name: "idx_glass_pricing_tier_range",
                table: "GlassPricingTiers",
                columns: new[] { "GlassId", "PowerMin", "PowerMax" });

            migrationBuilder.CreateIndex(
                name: "IX_GlassSupplements_SupplementId",
                table: "GlassSupplements",
                column: "SupplementId");

            migrationBuilder.CreateIndex(
                name: "idx_notifications_active_low_stock_unique",
                table: "Notifications",
                columns: new[] { "Type", "EntityType", "EntityId" },
                unique: true,
                filter: "\"Type\" = 'LowStock' AND \"EntityType\" = 'Product' AND \"EntityId\" IS NOT NULL AND \"ResolvedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_CreatedAt",
                table: "Notifications",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_IsRead",
                table: "Notifications",
                column: "IsRead");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_Type",
                table: "Notifications",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_OrderId",
                table: "OrderItems",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_ProductId",
                table: "OrderItems",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "idx_orders_order_number_unique",
                table: "Orders",
                column: "OrderNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_orders_sale_id",
                table: "Orders",
                column: "SaleId");

            migrationBuilder.CreateIndex(
                name: "idx_orders_status",
                table: "Orders",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Prescriptions_CustomerId",
                table: "Prescriptions",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "idx_product_categories_name_unique",
                table: "ProductCategories",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_products_category_id",
                table: "Products",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "idx_products_normalized_reference_unique",
                table: "Products",
                column: "NormalizedReference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Products_SupplierId",
                table: "Products",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleItems_ProductId",
                table: "SaleItems",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleItems_SaleId",
                table: "SaleItems",
                column: "SaleId");

            migrationBuilder.CreateIndex(
                name: "idx_sales_sale_date",
                table: "Sales",
                column: "SaleDate");

            migrationBuilder.CreateIndex(
                name: "idx_sales_sale_number_unique",
                table: "Sales",
                column: "SaleNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sales_CustomerId",
                table: "Sales",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Sales_StaffId",
                table: "Sales",
                column: "StaffId");

            migrationBuilder.CreateIndex(
                name: "idx_stock_movements_product_id",
                table: "StockMovements",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_PerformedByUserId",
                table: "StockMovements",
                column: "PerformedByUserId");

            migrationBuilder.CreateIndex(
                name: "idx_users_normalized_username_unique",
                table: "Users",
                column: "NormalizedUsername",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_workshop_sheet_items_sheet_id",
                table: "WorkshopSheetItems",
                column: "WorkshopSheetId");

            migrationBuilder.CreateIndex(
                name: "idx_workshop_sheets_current_unique",
                table: "WorkshopSheets",
                column: "OrderId",
                unique: true,
                filter: "\"IsCurrent\"");

            migrationBuilder.CreateIndex(
                name: "idx_workshop_sheets_order_version_unique",
                table: "WorkshopSheets",
                columns: new[] { "OrderId", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccessoryDetails");

            migrationBuilder.DropTable(
                name: "DocumentSequences");

            migrationBuilder.DropTable(
                name: "GlassPricingTiers");

            migrationBuilder.DropTable(
                name: "GlassSupplements");

            migrationBuilder.DropTable(
                name: "LensDetails");

            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropTable(
                name: "OrderItems");

            migrationBuilder.DropTable(
                name: "Prescriptions");

            migrationBuilder.DropTable(
                name: "SaleItems");

            migrationBuilder.DropTable(
                name: "StockMovements");

            migrationBuilder.DropTable(
                name: "WorkshopSheetItems");

            migrationBuilder.DropTable(
                name: "GlassDetails");

            migrationBuilder.DropTable(
                name: "Supplements");

            migrationBuilder.DropTable(
                name: "WorkshopSheets");

            migrationBuilder.DropTable(
                name: "Products");

            migrationBuilder.DropTable(
                name: "Orders");

            migrationBuilder.DropTable(
                name: "ProductCategories");

            migrationBuilder.DropTable(
                name: "Suppliers");

            migrationBuilder.DropTable(
                name: "Sales");

            migrationBuilder.DropTable(
                name: "Customers");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
