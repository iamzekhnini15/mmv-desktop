using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MMV.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ProductSchemaRefactoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Products_Suppliers_SupplierId",
                table: "Products");

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "Users",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 2, 1, 18, 11, 36, 583, DateTimeKind.Utc).AddTicks(4563),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValue: new DateTime(2026, 1, 29, 19, 20, 0, 776, DateTimeKind.Utc).AddTicks(996));

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "StockMovements",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 2, 1, 18, 11, 36, 589, DateTimeKind.Utc).AddTicks(9539),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValue: new DateTime(2026, 1, 29, 19, 20, 0, 778, DateTimeKind.Utc).AddTicks(9689));

            migrationBuilder.AlterColumn<DateTime>(
                name: "SaleDate",
                table: "Sales",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 2, 1, 18, 11, 36, 589, DateTimeKind.Utc).AddTicks(3658),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValue: new DateTime(2026, 1, 29, 19, 20, 0, 778, DateTimeKind.Utc).AddTicks(5348));

            migrationBuilder.AlterColumn<long>(
                name: "SupplierId",
                table: "Products",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "Products",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "RecommendedPrice",
                table: "Products",
                type: "REAL",
                nullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "Prescriptions",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 2, 1, 18, 11, 36, 588, DateTimeKind.Utc).AddTicks(5770),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValue: new DateTime(2026, 1, 29, 19, 20, 0, 777, DateTimeKind.Utc).AddTicks(9831));

            migrationBuilder.AlterColumn<DateTime>(
                name: "OrderDate",
                table: "Orders",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 2, 1, 18, 11, 36, 588, DateTimeKind.Utc).AddTicks(7296),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValue: new DateTime(2026, 1, 29, 19, 20, 0, 778, DateTimeKind.Utc).AddTicks(987));

            migrationBuilder.AlterColumn<DateTime>(
                name: "UpdatedAt",
                table: "Customers",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 2, 1, 18, 11, 36, 587, DateTimeKind.Utc).AddTicks(9332),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValue: new DateTime(2026, 1, 29, 19, 20, 0, 777, DateTimeKind.Utc).AddTicks(5960));

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "Customers",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 2, 1, 18, 11, 36, 587, DateTimeKind.Utc).AddTicks(8824),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValue: new DateTime(2026, 1, 29, 19, 20, 0, 777, DateTimeKind.Utc).AddTicks(5666));

            migrationBuilder.CreateTable(
                name: "AccessoryDetails",
                columns: table => new
                {
                    ProductId = table.Column<long>(type: "INTEGER", nullable: false),
                    Color = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Size = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    Material = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true)
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
                    ProductId = table.Column<long>(type: "INTEGER", nullable: false),
                    Material = table.Column<string>(type: "TEXT", nullable: true),
                    GlassType = table.Column<string>(type: "TEXT", nullable: true),
                    Diameter = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    Index = table.Column<decimal>(type: "TEXT", precision: 3, scale: 2, nullable: true),
                    PowerLimitMin = table.Column<decimal>(type: "TEXT", precision: 5, scale: 2, nullable: true),
                    PowerLimitMax = table.Column<decimal>(type: "TEXT", precision: 5, scale: 2, nullable: true)
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
                    ProductId = table.Column<long>(type: "INTEGER", nullable: false),
                    Brand = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Model = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Material = table.Column<string>(type: "TEXT", nullable: true),
                    LensType = table.Column<string>(type: "TEXT", nullable: true),
                    Diameter = table.Column<decimal>(type: "TEXT", precision: 4, scale: 2, nullable: true),
                    BaseCurve = table.Column<decimal>(type: "TEXT", precision: 4, scale: 2, nullable: true),
                    IsColored = table.Column<bool>(type: "INTEGER", nullable: false),
                    Duration = table.Column<string>(type: "TEXT", nullable: true)
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
                name: "Supplements",
                columns: table => new
                {
                    SupplementId = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    SupplementPrice = table.Column<decimal>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Supplements", x => x.SupplementId);
                });

            migrationBuilder.CreateTable(
                name: "GlassPricingTiers",
                columns: table => new
                {
                    TierId = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GlassId = table.Column<long>(type: "INTEGER", nullable: false),
                    PowerMin = table.Column<decimal>(type: "TEXT", precision: 5, scale: 2, nullable: false),
                    PowerMax = table.Column<decimal>(type: "TEXT", precision: 5, scale: 2, nullable: false),
                    PurchasePriceGrid = table.Column<decimal>(type: "TEXT", nullable: false),
                    SalePriceGrid = table.Column<decimal>(type: "TEXT", nullable: false)
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
                    GlassId = table.Column<long>(type: "INTEGER", nullable: false),
                    SupplementId = table.Column<long>(type: "INTEGER", nullable: false)
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

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "UserId",
                keyValue: 1L,
                column: "CreatedAt",
                value: new DateTime(2026, 2, 1, 18, 11, 36, 590, DateTimeKind.Utc).AddTicks(767));

            migrationBuilder.CreateIndex(
                name: "idx_glass_pricing_tier_range",
                table: "GlassPricingTiers",
                columns: new[] { "GlassId", "PowerMin", "PowerMax" });

            migrationBuilder.CreateIndex(
                name: "IX_GlassSupplements_SupplementId",
                table: "GlassSupplements",
                column: "SupplementId");

            migrationBuilder.AddForeignKey(
                name: "FK_Products_Suppliers_SupplierId",
                table: "Products",
                column: "SupplierId",
                principalTable: "Suppliers",
                principalColumn: "SupplierId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Products_Suppliers_SupplierId",
                table: "Products");

            migrationBuilder.DropTable(
                name: "AccessoryDetails");

            migrationBuilder.DropTable(
                name: "GlassPricingTiers");

            migrationBuilder.DropTable(
                name: "GlassSupplements");

            migrationBuilder.DropTable(
                name: "LensDetails");

            migrationBuilder.DropTable(
                name: "GlassDetails");

            migrationBuilder.DropTable(
                name: "Supplements");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "RecommendedPrice",
                table: "Products");

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "Users",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 1, 29, 19, 20, 0, 776, DateTimeKind.Utc).AddTicks(996),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValue: new DateTime(2026, 2, 1, 18, 11, 36, 583, DateTimeKind.Utc).AddTicks(4563));

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "StockMovements",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 1, 29, 19, 20, 0, 778, DateTimeKind.Utc).AddTicks(9689),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValue: new DateTime(2026, 2, 1, 18, 11, 36, 589, DateTimeKind.Utc).AddTicks(9539));

            migrationBuilder.AlterColumn<DateTime>(
                name: "SaleDate",
                table: "Sales",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 1, 29, 19, 20, 0, 778, DateTimeKind.Utc).AddTicks(5348),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValue: new DateTime(2026, 2, 1, 18, 11, 36, 589, DateTimeKind.Utc).AddTicks(3658));

            migrationBuilder.AlterColumn<long>(
                name: "SupplierId",
                table: "Products",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "Prescriptions",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 1, 29, 19, 20, 0, 777, DateTimeKind.Utc).AddTicks(9831),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValue: new DateTime(2026, 2, 1, 18, 11, 36, 588, DateTimeKind.Utc).AddTicks(5770));

            migrationBuilder.AlterColumn<DateTime>(
                name: "OrderDate",
                table: "Orders",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 1, 29, 19, 20, 0, 778, DateTimeKind.Utc).AddTicks(987),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValue: new DateTime(2026, 2, 1, 18, 11, 36, 588, DateTimeKind.Utc).AddTicks(7296));

            migrationBuilder.AlterColumn<DateTime>(
                name: "UpdatedAt",
                table: "Customers",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 1, 29, 19, 20, 0, 777, DateTimeKind.Utc).AddTicks(5960),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValue: new DateTime(2026, 2, 1, 18, 11, 36, 587, DateTimeKind.Utc).AddTicks(9332));

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "Customers",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 1, 29, 19, 20, 0, 777, DateTimeKind.Utc).AddTicks(5666),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValue: new DateTime(2026, 2, 1, 18, 11, 36, 587, DateTimeKind.Utc).AddTicks(8824));

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "UserId",
                keyValue: 1L,
                column: "CreatedAt",
                value: new DateTime(2026, 1, 29, 19, 20, 0, 779, DateTimeKind.Utc).AddTicks(659));

            migrationBuilder.AddForeignKey(
                name: "FK_Products_Suppliers_SupplierId",
                table: "Products",
                column: "SupplierId",
                principalTable: "Suppliers",
                principalColumn: "SupplierId",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
