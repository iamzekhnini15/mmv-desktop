using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MMV.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkshopSheets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkshopSheets",
                columns: table => new
                {
                    WorkshopSheetId = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OrderId = table.Column<long>(type: "INTEGER", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    IsCurrent = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TechnicalFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    OrderNumberSnapshot = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    OrderDateSnapshot = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EstimatedDeliverySnapshot = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CustomerNameSnapshot = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    InstructionsSnapshot = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    QcStatus = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    QcComment = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    QcCompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
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
                    WorkshopSheetItemId = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WorkshopSheetId = table.Column<long>(type: "INTEGER", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    ItemType = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    SourceProductId = table.Column<long>(type: "INTEGER", nullable: true),
                    ProductReferenceSnapshot = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    ProductNameSnapshot = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    ProductCategorySnapshot = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    UsageType = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    SourceSphere = table.Column<double>(type: "REAL", nullable: true),
                    SourceCylinder = table.Column<double>(type: "REAL", nullable: true),
                    SourceAxis = table.Column<int>(type: "INTEGER", nullable: true),
                    Addition = table.Column<double>(type: "REAL", nullable: true),
                    PrismValue = table.Column<double>(type: "REAL", nullable: true),
                    PrismBase = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    VisualAcuity = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    TransposedSphere = table.Column<double>(type: "REAL", nullable: true),
                    TransposedCylinder = table.Column<double>(type: "REAL", nullable: true),
                    TransposedAxis = table.Column<int>(type: "INTEGER", nullable: true),
                    HasTransposition = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false)
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

            migrationBuilder.CreateIndex(
                name: "idx_workshop_sheet_items_sheet_id",
                table: "WorkshopSheetItems",
                column: "WorkshopSheetId");

            migrationBuilder.CreateIndex(
                name: "idx_workshop_sheets_current_unique",
                table: "WorkshopSheets",
                column: "OrderId",
                unique: true,
                filter: "\"IsCurrent\" = 1");

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
                name: "WorkshopSheetItems");

            migrationBuilder.DropTable(
                name: "WorkshopSheets");
        }
    }
}
