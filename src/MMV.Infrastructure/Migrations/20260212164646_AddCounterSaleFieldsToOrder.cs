using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MMV.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCounterSaleFieldsToOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Users",
                keyColumn: "UserId",
                keyValue: 1L);

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "Users",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 2, 12, 16, 46, 46, 440, DateTimeKind.Utc).AddTicks(5101),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValue: new DateTime(2026, 2, 2, 0, 50, 50, 124, DateTimeKind.Utc).AddTicks(872));

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "StockMovements",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 2, 12, 16, 46, 46, 445, DateTimeKind.Utc).AddTicks(3134),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValue: new DateTime(2026, 2, 2, 0, 50, 50, 129, DateTimeKind.Utc).AddTicks(9573));

            migrationBuilder.AlterColumn<DateTime>(
                name: "SaleDate",
                table: "Sales",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 2, 12, 16, 46, 46, 444, DateTimeKind.Utc).AddTicks(8618),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValue: new DateTime(2026, 2, 2, 0, 50, 50, 129, DateTimeKind.Utc).AddTicks(4136));

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "Prescriptions",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 2, 12, 16, 46, 46, 444, DateTimeKind.Utc).AddTicks(2892),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValue: new DateTime(2026, 2, 2, 0, 50, 50, 128, DateTimeKind.Utc).AddTicks(7322));

            migrationBuilder.AlterColumn<DateTime>(
                name: "OrderDate",
                table: "Orders",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 2, 12, 16, 46, 46, 444, DateTimeKind.Utc).AddTicks(3832),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValue: new DateTime(2026, 2, 2, 0, 50, 50, 128, DateTimeKind.Utc).AddTicks(8562));

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

            migrationBuilder.AddColumn<bool>(
                name: "IsCounterSale",
                table: "Orders",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "PaymentMethod",
                table: "Orders",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "UpdatedAt",
                table: "Customers",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 2, 12, 16, 46, 46, 443, DateTimeKind.Utc).AddTicks(8346),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValue: new DateTime(2026, 2, 2, 0, 50, 50, 128, DateTimeKind.Utc).AddTicks(2278));

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "Customers",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 2, 12, 16, 46, 46, 443, DateTimeKind.Utc).AddTicks(7944),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValue: new DateTime(2026, 2, 2, 0, 50, 50, 128, DateTimeKind.Utc).AddTicks(1963));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DiscountAmount",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "FinalAmount",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "IsCounterSale",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PaymentMethod",
                table: "Orders");

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "Users",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 2, 2, 0, 50, 50, 124, DateTimeKind.Utc).AddTicks(872),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValue: new DateTime(2026, 2, 12, 16, 46, 46, 440, DateTimeKind.Utc).AddTicks(5101));

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "StockMovements",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 2, 2, 0, 50, 50, 129, DateTimeKind.Utc).AddTicks(9573),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValue: new DateTime(2026, 2, 12, 16, 46, 46, 445, DateTimeKind.Utc).AddTicks(3134));

            migrationBuilder.AlterColumn<DateTime>(
                name: "SaleDate",
                table: "Sales",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 2, 2, 0, 50, 50, 129, DateTimeKind.Utc).AddTicks(4136),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValue: new DateTime(2026, 2, 12, 16, 46, 46, 444, DateTimeKind.Utc).AddTicks(8618));

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "Prescriptions",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 2, 2, 0, 50, 50, 128, DateTimeKind.Utc).AddTicks(7322),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValue: new DateTime(2026, 2, 12, 16, 46, 46, 444, DateTimeKind.Utc).AddTicks(2892));

            migrationBuilder.AlterColumn<DateTime>(
                name: "OrderDate",
                table: "Orders",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 2, 2, 0, 50, 50, 128, DateTimeKind.Utc).AddTicks(8562),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValue: new DateTime(2026, 2, 12, 16, 46, 46, 444, DateTimeKind.Utc).AddTicks(3832));

            migrationBuilder.AlterColumn<DateTime>(
                name: "UpdatedAt",
                table: "Customers",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 2, 2, 0, 50, 50, 128, DateTimeKind.Utc).AddTicks(2278),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValue: new DateTime(2026, 2, 12, 16, 46, 46, 443, DateTimeKind.Utc).AddTicks(8346));

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "Customers",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 2, 2, 0, 50, 50, 128, DateTimeKind.Utc).AddTicks(1963),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValue: new DateTime(2026, 2, 12, 16, 46, 46, 443, DateTimeKind.Utc).AddTicks(7944));

            migrationBuilder.InsertData(
                table: "Users",
                columns: new[] { "UserId", "CreatedAt", "FirstName", "IsActive", "LastLogin", "LastName", "PasswordHash", "Role", "Username" },
                values: new object[] { 1L, new DateTime(2026, 2, 2, 0, 50, 50, 130, DateTimeKind.Utc).AddTicks(1708), "Administrateur", true, null, "Système", "$2a$11$dXJ3SW6G7P50eS6xFJwFHeJ/hbtjiZlyCloO/sURR8EZ4/nqXJcOy", "Admin", "admin" });
        }
    }
}
