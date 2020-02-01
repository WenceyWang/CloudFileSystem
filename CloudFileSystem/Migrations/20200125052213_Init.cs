using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DreamRecorder.CloudFileSystem.Migrations
{
    public partial class Init : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BlockMetadata",
                columns: table => new
                {
                    Guid = table.Column<Guid>(nullable: false),
                    RemoteFileId = table.Column<string>(nullable: true),
                    File = table.Column<Guid>(nullable: false),
                    BlockSequence = table.Column<long>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlockMetadata", x => x.Guid);
                });

            migrationBuilder.CreateTable(
                name: "FileMetadata",
                columns: table => new
                {
                    Guid = table.Column<Guid>(nullable: false),
                    Name = table.Column<string>(nullable: false),
                    Size = table.Column<long>(nullable: false),
                    AllocatedBlockCount = table.Column<int>(nullable: false),
                    Attributes = table.Column<long>(nullable: false),
                    ReparseTag = table.Column<long>(nullable: false),
                    CreationTime = table.Column<decimal>(nullable: false),
                    LastAccessTime = table.Column<decimal>(nullable: false),
                    LastWriteTime = table.Column<decimal>(nullable: false),
                    ChangeTime = table.Column<decimal>(nullable: false),
                    IndexNumber = table.Column<decimal>(nullable: false),
                    HardLinks = table.Column<long>(nullable: false),
                    SecurityInfo = table.Column<byte[]>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileMetadata", x => x.Guid);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BlockMetadata");

            migrationBuilder.DropTable(
                name: "FileMetadata");
        }
    }
}
