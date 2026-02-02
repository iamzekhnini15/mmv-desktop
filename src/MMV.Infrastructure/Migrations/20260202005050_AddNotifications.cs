using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MMV.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "Users",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 2, 2, 0, 50, 50, 124, DateTimeKind.Utc).AddTicks(872),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValue: new DateTime(2026, 2, 1, 18, 11, 36, 583, DateTimeKind.Utc).AddTicks(4563));

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "StockMovements",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 2, 2, 0, 50, 50, 129, DateTimeKind.Utc).AddTicks(9573),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValue: new DateTime(2026, 2, 1, 18, 11, 36, 589, DateTimeKind.Utc).AddTicks(9539));

            migrationBuilder.AlterColumn<DateTime>(
                name: "SaleDate",
                table: "Sales",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 2, 2, 0, 50, 50, 129, DateTimeKind.Utc).AddTicks(4136),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValue: new DateTime(2026, 2, 1, 18, 11, 36, 589, DateTimeKind.Utc).AddTicks(3658));

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "Prescriptions",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 2, 2, 0, 50, 50, 128, DateTimeKind.Utc).AddTicks(7322),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValue: new DateTime(2026, 2, 1, 18, 11, 36, 588, DateTimeKind.Utc).AddTicks(5770));

            migrationBuilder.AlterColumn<DateTime>(
                name: "OrderDate",
                table: "Orders",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 2, 2, 0, 50, 50, 128, DateTimeKind.Utc).AddTicks(8562),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValue: new DateTime(2026, 2, 1, 18, 11, 36, 588, DateTimeKind.Utc).AddTicks(7296));

            migrationBuilder.AlterColumn<DateTime>(
                name: "UpdatedAt",
                table: "Customers",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 2, 2, 0, 50, 50, 128, DateTimeKind.Utc).AddTicks(2278),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValue: new DateTime(2026, 2, 1, 18, 11, 36, 587, DateTimeKind.Utc).AddTicks(9332));

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "Customers",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 2, 2, 0, 50, 50, 128, DateTimeKind.Utc).AddTicks(1963),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValue: new DateTime(2026, 2, 1, 18, 11, 36, 587, DateTimeKind.Utc).AddTicks(8824));

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    NotificationId = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Type = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    EntityId = table.Column<long>(type: "INTEGER", nullable: true),
                    EntityType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    IsRead = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.NotificationId);
                });

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "UserId",
                keyValue: 1L,
                column: "CreatedAt",
                value: new DateTime(2026, 2, 2, 0, 50, 50, 130, DateTimeKind.Utc).AddTicks(1708));

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "Users",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 2, 1, 18, 11, 36, 583, DateTimeKind.Utc).AddTicks(4563),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValue: new DateTime(2026, 2, 2, 0, 50, 50, 124, DateTimeKind.Utc).AddTicks(872));

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "StockMovements",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 2, 1, 18, 11, 36, 589, DateTimeKind.Utc).AddTicks(9539),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValue: new DateTime(2026, 2, 2, 0, 50, 50, 129, DateTimeKind.Utc).AddTicks(9573));

            migrationBuilder.AlterColumn<DateTime>(
                name: "SaleDate",
                table: "Sales",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 2, 1, 18, 11, 36, 589, DateTimeKind.Utc).AddTicks(3658),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValue: new DateTime(2026, 2, 2, 0, 50, 50, 129, DateTimeKind.Utc).AddTicks(4136));

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "Prescriptions",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 2, 1, 18, 11, 36, 588, DateTimeKind.Utc).AddTicks(5770),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValue: new DateTime(2026, 2, 2, 0, 50, 50, 128, DateTimeKind.Utc).AddTicks(7322));

            migrationBuilder.AlterColumn<DateTime>(
                name: "OrderDate",
                table: "Orders",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 2, 1, 18, 11, 36, 588, DateTimeKind.Utc).AddTicks(7296),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValue: new DateTime(2026, 2, 2, 0, 50, 50, 128, DateTimeKind.Utc).AddTicks(8562));

            migrationBuilder.AlterColumn<DateTime>(
                name: "UpdatedAt",
                table: "Customers",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 2, 1, 18, 11, 36, 587, DateTimeKind.Utc).AddTicks(9332),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValue: new DateTime(2026, 2, 2, 0, 50, 50, 128, DateTimeKind.Utc).AddTicks(2278));

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "Customers",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 2, 1, 18, 11, 36, 587, DateTimeKind.Utc).AddTicks(8824),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValue: new DateTime(2026, 2, 2, 0, 50, 50, 128, DateTimeKind.Utc).AddTicks(1963));

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "UserId",
                keyValue: 1L,
                column: "CreatedAt",
                value: new DateTime(2026, 2, 1, 18, 11, 36, 590, DateTimeKind.Utc).AddTicks(767));
        }
    }
}
