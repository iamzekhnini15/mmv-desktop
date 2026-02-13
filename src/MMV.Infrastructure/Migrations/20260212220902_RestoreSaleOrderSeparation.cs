using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MMV.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RestoreSaleOrderSeparation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Orders_Customers_CustomerId",
                table: "Orders");

            migrationBuilder.DropForeignKey(
                name: "FK_Orders_Users_StaffId",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "idx_orders_customer_id",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_StaffId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "CustomerId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "DepositAmount",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "DiscountAmount",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "FinalAmount",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PaymentMethod",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "TotalAmount",
                table: "Orders");

            migrationBuilder.RenameColumn(
                name: "StaffId",
                table: "Orders",
                newName: "SupplierId");

            migrationBuilder.RenameColumn(
                name: "RemainingAmount",
                table: "Orders",
                newName: "ReceivedDate");

            migrationBuilder.RenameColumn(
                name: "IsCounterSale",
                table: "Orders",
                newName: "SaleId");

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "Users",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 2, 12, 22, 9, 2, 279, DateTimeKind.Utc).AddTicks(2902),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValue: new DateTime(2026, 2, 12, 17, 33, 13, 16, DateTimeKind.Utc).AddTicks(5128));

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "StockMovements",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 2, 12, 22, 9, 2, 284, DateTimeKind.Utc).AddTicks(119),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValue: new DateTime(2026, 2, 12, 17, 33, 13, 21, DateTimeKind.Utc).AddTicks(1913));

            migrationBuilder.AlterColumn<DateTime>(
                name: "SaleDate",
                table: "Sales",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 2, 12, 22, 9, 2, 283, DateTimeKind.Utc).AddTicks(4405),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValue: new DateTime(2026, 2, 12, 17, 33, 13, 20, DateTimeKind.Utc).AddTicks(7863));

            migrationBuilder.AddColumn<decimal>(
                name: "DepositAmount",
                table: "Sales",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EstimatedDelivery",
                table: "Sales",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RemainingAmount",
                table: "Sales",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Sales",
                type: "TEXT",
                nullable: false,
                defaultValue: "Draft");

            migrationBuilder.AddColumn<double>(
                name: "Addition",
                table: "SaleItems",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Axis",
                table: "SaleItems",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Cylinder",
                table: "SaleItems",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ItemType",
                table: "SaleItems",
                type: "TEXT",
                nullable: false,
                defaultValue: "Frame");

            migrationBuilder.AddColumn<string>(
                name: "PrismBase",
                table: "SaleItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "PrismValue",
                table: "SaleItems",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Sphere",
                table: "SaleItems",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UsageType",
                table: "SaleItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VisualAcuity",
                table: "SaleItems",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "Prescriptions",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 2, 12, 22, 9, 2, 282, DateTimeKind.Utc).AddTicks(8445),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValue: new DateTime(2026, 2, 12, 17, 33, 13, 20, DateTimeKind.Utc).AddTicks(2339));

            migrationBuilder.AlterColumn<DateTime>(
                name: "OrderDate",
                table: "Orders",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 2, 12, 22, 9, 2, 282, DateTimeKind.Utc).AddTicks(9524),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValue: new DateTime(2026, 2, 12, 17, 33, 13, 20, DateTimeKind.Utc).AddTicks(3656));

            migrationBuilder.AlterColumn<DateTime>(
                name: "UpdatedAt",
                table: "Customers",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 2, 12, 22, 9, 2, 282, DateTimeKind.Utc).AddTicks(5262),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValue: new DateTime(2026, 2, 12, 17, 33, 13, 19, DateTimeKind.Utc).AddTicks(7704));

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "Customers",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 2, 12, 22, 9, 2, 282, DateTimeKind.Utc).AddTicks(4997),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValue: new DateTime(2026, 2, 12, 17, 33, 13, 19, DateTimeKind.Utc).AddTicks(7449));

            migrationBuilder.CreateIndex(
                name: "idx_orders_sale_id",
                table: "Orders",
                column: "SaleId");

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_Sales_SaleId",
                table: "Orders",
                column: "SaleId",
                principalTable: "Sales",
                principalColumn: "SaleId",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Orders_Sales_SaleId",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "idx_orders_sale_id",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "DepositAmount",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "EstimatedDelivery",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "RemainingAmount",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "Addition",
                table: "SaleItems");

            migrationBuilder.DropColumn(
                name: "Axis",
                table: "SaleItems");

            migrationBuilder.DropColumn(
                name: "Cylinder",
                table: "SaleItems");

            migrationBuilder.DropColumn(
                name: "ItemType",
                table: "SaleItems");

            migrationBuilder.DropColumn(
                name: "PrismBase",
                table: "SaleItems");

            migrationBuilder.DropColumn(
                name: "PrismValue",
                table: "SaleItems");

            migrationBuilder.DropColumn(
                name: "Sphere",
                table: "SaleItems");

            migrationBuilder.DropColumn(
                name: "UsageType",
                table: "SaleItems");

            migrationBuilder.DropColumn(
                name: "VisualAcuity",
                table: "SaleItems");

            migrationBuilder.RenameColumn(
                name: "SupplierId",
                table: "Orders",
                newName: "StaffId");

            migrationBuilder.RenameColumn(
                name: "SaleId",
                table: "Orders",
                newName: "IsCounterSale");

            migrationBuilder.RenameColumn(
                name: "ReceivedDate",
                table: "Orders",
                newName: "RemainingAmount");

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "Users",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 2, 12, 17, 33, 13, 16, DateTimeKind.Utc).AddTicks(5128),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValue: new DateTime(2026, 2, 12, 22, 9, 2, 279, DateTimeKind.Utc).AddTicks(2902));

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "StockMovements",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 2, 12, 17, 33, 13, 21, DateTimeKind.Utc).AddTicks(1913),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValue: new DateTime(2026, 2, 12, 22, 9, 2, 284, DateTimeKind.Utc).AddTicks(119));

            migrationBuilder.AlterColumn<DateTime>(
                name: "SaleDate",
                table: "Sales",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 2, 12, 17, 33, 13, 20, DateTimeKind.Utc).AddTicks(7863),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValue: new DateTime(2026, 2, 12, 22, 9, 2, 283, DateTimeKind.Utc).AddTicks(4405));

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "Prescriptions",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 2, 12, 17, 33, 13, 20, DateTimeKind.Utc).AddTicks(2339),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValue: new DateTime(2026, 2, 12, 22, 9, 2, 282, DateTimeKind.Utc).AddTicks(8445));

            migrationBuilder.AlterColumn<DateTime>(
                name: "OrderDate",
                table: "Orders",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 2, 12, 17, 33, 13, 20, DateTimeKind.Utc).AddTicks(3656),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValue: new DateTime(2026, 2, 12, 22, 9, 2, 282, DateTimeKind.Utc).AddTicks(9524));

            migrationBuilder.AddColumn<long>(
                name: "CustomerId",
                table: "Orders",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DepositAmount",
                table: "Orders",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountAmount",
                table: "Orders",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FinalAmount",
                table: "Orders",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PaymentMethod",
                table: "Orders",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalAmount",
                table: "Orders",
                type: "REAL",
                nullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "UpdatedAt",
                table: "Customers",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 2, 12, 17, 33, 13, 19, DateTimeKind.Utc).AddTicks(7704),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValue: new DateTime(2026, 2, 12, 22, 9, 2, 282, DateTimeKind.Utc).AddTicks(5262));

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "Customers",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 2, 12, 17, 33, 13, 19, DateTimeKind.Utc).AddTicks(7449),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValue: new DateTime(2026, 2, 12, 22, 9, 2, 282, DateTimeKind.Utc).AddTicks(4997));

            migrationBuilder.CreateIndex(
                name: "idx_orders_customer_id",
                table: "Orders",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_StaffId",
                table: "Orders",
                column: "StaffId");

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_Customers_CustomerId",
                table: "Orders",
                column: "CustomerId",
                principalTable: "Customers",
                principalColumn: "CustomerId",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_Users_StaffId",
                table: "Orders",
                column: "StaffId",
                principalTable: "Users",
                principalColumn: "UserId",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
