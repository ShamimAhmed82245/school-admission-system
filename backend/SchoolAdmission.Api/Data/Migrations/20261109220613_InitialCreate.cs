using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolAdmission.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Classes",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TotalSeats = table.Column<int>(type: "integer", nullable: false),
                    AvailableSeats = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Classes", x => x.Id);
                    table.CheckConstraint("CK_Classes_AvailableSeats_Lte_TotalSeats", "\"AvailableSeats\" <= \"TotalSeats\"");
                    table.CheckConstraint("CK_Classes_AvailableSeats_NonNegative", "\"AvailableSeats\" >= 0");
                    table.CheckConstraint("CK_Classes_TotalSeats_Positive", "\"TotalSeats\" > 0");
                });

            migrationBuilder.CreateTable(
                name: "Holds",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StudentId = table.Column<string>(type: "character varying(17)", maxLength: 17, nullable: false),
                    ClassId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Holds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Holds_Classes_ClassId",
                        column: x => x.ClassId,
                        principalTable: "Classes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Holds_ClassId",
                table: "Holds",
                column: "ClassId");

            migrationBuilder.CreateIndex(
                name: "IX_Holds_ExpiresAt",
                table: "Holds",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_Holds_Status",
                table: "Holds",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Holds_StudentId_ClassId_ActiveOrConfirmed",
                table: "Holds",
                columns: new[] { "StudentId", "ClassId" },
                unique: true,
                filter: "\"Status\" IN ('HELD', 'CONFIRMED')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Holds");

            migrationBuilder.DropTable(
                name: "Classes");
        }
    }
}
