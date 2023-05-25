﻿// <auto-generated />
using System;
using Jellyfin.Plugin.MediaAnalyzer;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace Jellyfin.Plugin.MediaAnalyzer.Migrations
{
    [DbContext(typeof(MediaAnalyzerDbContext))]
    [Migration("20230525091047_CreateBlacklistSegment")]
    partial class CreateBlacklistSegment
    {
        /// <inheritdoc />
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder.HasAnnotation("ProductVersion", "7.0.5");

            modelBuilder.Entity("Jellyfin.Plugin.MediaAnalyzer.BlacklistSegment", b =>
                {
                    b.Property<Guid>("ItemId")
                        .HasColumnType("TEXT");

                    b.Property<int>("Type")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.HasKey("ItemId", "Type");

                    b.ToTable("BlacklistSegment");
                });
#pragma warning restore 612, 618
        }
    }
}
