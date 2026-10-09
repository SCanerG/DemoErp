using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Demo.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReportingIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Orders_CompletedAt",
                table: "Orders",
                column: "CompletedAt",
                filter: "\"Status\" = 'Completed' AND \"CompletedAt\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_OrderDate_Id",
                table: "Orders",
                columns: new[] { "OrderDate", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Orders_CompletedAt",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_OrderDate_Id",
                table: "Orders");
        }
    }
}
