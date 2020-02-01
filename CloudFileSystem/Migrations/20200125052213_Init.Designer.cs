﻿// <auto-generated />
using System;
using DreamRecorder.CloudFileSystem;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DreamRecorder.CloudFileSystem.Migrations
{
    [DbContext(typeof(DataContext))]
    [Migration("20200125052213_Init")]
    partial class Init
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "3.1.1")
                .HasAnnotation("Relational:MaxIdentifierLength", 128)
                .HasAnnotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn);

            modelBuilder.Entity("DreamRecorder.CloudFileSystem.BlockMetadata", b =>
                {
                    b.Property<Guid>("Guid")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<long>("BlockSequence")
                        .HasColumnType("bigint");

                    b.Property<Guid>("File")
                        .HasColumnType("uniqueidentifier");

                    b.Property<string>("RemoteFileId")
                        .HasColumnType("nvarchar(max)");

                    b.HasKey("Guid");

                    b.ToTable("BlockMetadata");
                });

            modelBuilder.Entity("DreamRecorder.CloudFileSystem.FileMetadata", b =>
                {
                    b.Property<Guid>("Guid")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<int>("AllocatedBlockCount")
                        .HasColumnType("int");

                    b.Property<long>("Attributes")
                        .HasColumnType("bigint");

                    b.Property<decimal>("ChangeTime")
                        .HasColumnType("decimal(20,0)");

                    b.Property<decimal>("CreationTime")
                        .HasColumnType("decimal(20,0)");

                    b.Property<long>("HardLinks")
                        .HasColumnType("bigint");

                    b.Property<decimal>("IndexNumber")
                        .HasColumnType("decimal(20,0)");

                    b.Property<decimal>("LastAccessTime")
                        .HasColumnType("decimal(20,0)");

                    b.Property<decimal>("LastWriteTime")
                        .HasColumnType("decimal(20,0)");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<long>("ReparseTag")
                        .HasColumnType("bigint");

                    b.Property<byte[]>("SecurityInfo")
                        .IsRequired()
                        .HasColumnType("varbinary(max)");

                    b.Property<long>("Size")
                        .HasColumnType("bigint");

                    b.HasKey("Guid");

                    b.ToTable("FileMetadata");
                });
#pragma warning restore 612, 618
        }
    }
}
