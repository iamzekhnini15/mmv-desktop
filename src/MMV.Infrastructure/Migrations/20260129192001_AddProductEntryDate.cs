using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MMV.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProductEntryDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "Users",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 1, 29, 19, 20, 0, 776, DateTimeKind.Utc).AddTicks(996),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValue: new DateTime(2026, 1, 27, 18, 45, 42, 646, DateTimeKind.Utc).AddTicks(8035));

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "StockMovements",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 1, 29, 19, 20, 0, 778, DateTimeKind.Utc).AddTicks(9689),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValue: new DateTime(2026, 1, 27, 18, 45, 42, 650, DateTimeKind.Utc).AddTicks(2108));

            migrationBuilder.AlterColumn<DateTime>(
                name: "SaleDate",
                table: "Sales",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 1, 29, 19, 20, 0, 778, DateTimeKind.Utc).AddTicks(5348),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValue: new DateTime(2026, 1, 27, 18, 45, 42, 649, DateTimeKind.Utc).AddTicks(6620));

            migrationBuilder.AddColumn<DateTime>(
                name: "EntryDate",
                table: "Products",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "Prescriptions",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 1, 29, 19, 20, 0, 777, DateTimeKind.Utc).AddTicks(9831),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValue: new DateTime(2026, 1, 27, 18, 45, 42, 648, DateTimeKind.Utc).AddTicks(9081));

            migrationBuilder.AlterColumn<DateTime>(
                name: "OrderDate",
                table: "Orders",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 1, 29, 19, 20, 0, 778, DateTimeKind.Utc).AddTicks(987),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValue: new DateTime(2026, 1, 27, 18, 45, 42, 649, DateTimeKind.Utc).AddTicks(345));

            migrationBuilder.AlterColumn<DateTime>(
                name: "UpdatedAt",
                table: "Customers",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 1, 29, 19, 20, 0, 777, DateTimeKind.Utc).AddTicks(5960),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValue: new DateTime(2026, 1, 27, 18, 45, 42, 648, DateTimeKind.Utc).AddTicks(4546));

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "Customers",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 1, 29, 19, 20, 0, 777, DateTimeKind.Utc).AddTicks(5666),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValue: new DateTime(2026, 1, 27, 18, 45, 42, 648, DateTimeKind.Utc).AddTicks(4257));

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "UserId",
                keyValue: 1L,
                column: "CreatedAt",
                value: new DateTime(2026, 1, 29, 19, 20, 0, 779, DateTimeKind.Utc).AddTicks(659));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EntryDate",
                table: "Products");

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "Users",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 1, 27, 18, 45, 42, 646, DateTimeKind.Utc).AddTicks(8035),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValue: new DateTime(2026, 1, 29, 19, 20, 0, 776, DateTimeKind.Utc).AddTicks(996));

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "StockMovements",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 1, 27, 18, 45, 42, 650, DateTimeKind.Utc).AddTicks(2108),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValue: new DateTime(2026, 1, 29, 19, 20, 0, 778, DateTimeKind.Utc).AddTicks(9689));

            migrationBuilder.AlterColumn<DateTime>(
                name: "SaleDate",
                table: "Sales",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 1, 27, 18, 45, 42, 649, DateTimeKind.Utc).AddTicks(6620),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValue: new DateTime(2026, 1, 29, 19, 20, 0, 778, DateTimeKind.Utc).AddTicks(5348));

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "Prescriptions",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 1, 27, 18, 45, 42, 648, DateTimeKind.Utc).AddTicks(9081),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValue: new DateTime(2026, 1, 29, 19, 20, 0, 777, DateTimeKind.Utc).AddTicks(9831));

            migrationBuilder.AlterColumn<DateTime>(
                name: "OrderDate",
                table: "Orders",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 1, 27, 18, 45, 42, 649, DateTimeKind.Utc).AddTicks(345),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValue: new DateTime(2026, 1, 29, 19, 20, 0, 778, DateTimeKind.Utc).AddTicks(987));

            migrationBuilder.AlterColumn<DateTime>(
                name: "UpdatedAt",
                table: "Customers",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 1, 27, 18, 45, 42, 648, DateTimeKind.Utc).AddTicks(4546),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValue: new DateTime(2026, 1, 29, 19, 20, 0, 777, DateTimeKind.Utc).AddTicks(5960));

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "Customers",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 1, 27, 18, 45, 42, 648, DateTimeKind.Utc).AddTicks(4257),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValue: new DateTime(2026, 1, 29, 19, 20, 0, 777, DateTimeKind.Utc).AddTicks(5666));

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "UserId",
                keyValue: 1L,
                column: "CreatedAt",
                value: new DateTime(2026, 1, 27, 18, 45, 42, 650, DateTimeKind.Utc).AddTicks(3042));
        }
    }
}
