using System ;
using System . Collections ;
using System . Collections . Generic ;
using System . Linq ;

using Microsoft . EntityFrameworkCore ;
using Microsoft . EntityFrameworkCore . Infrastructure ;
using Microsoft . EntityFrameworkCore . Metadata ;

namespace DreamRecorder . CloudFileSystem . Migrations
{

	[DbContext ( typeof ( DataContext ) )]
	internal partial class DataContextModelSnapshot : ModelSnapshot
	{

		protected override void BuildModel ( ModelBuilder modelBuilder )
		{
#pragma warning disable 612, 618
			modelBuilder . HasAnnotation ( "ProductVersion" , "3.1.1" ) .
						   HasAnnotation ( "Relational:MaxIdentifierLength" , 128 ) .
						   HasAnnotation (
										  "SqlServer:ValueGenerationStrategy" ,
										  SqlServerValueGenerationStrategy . IdentityColumn ) ;

			modelBuilder . Entity (
								   "DreamRecorder.CloudFileSystem.FileSystem.BlockMetadata" ,
								   b =>
								   {
									   b . Property <Guid> ( "Guid" ) .
										   ValueGeneratedOnAdd ( ) .
										   HasColumnType ( "uniqueidentifier" ) ;

									   b . Property <byte [ ]> ( "AesIV" ) .
										   HasColumnType ( "varbinary(max)" ) ;

									   b . Property <byte [ ]> ( "AesKey" ) .
										   HasColumnType ( "varbinary(max)" ) ;

									   b . Property <long> ( "BlockSequence" ) .
										   HasColumnType ( "bigint" ) ;

									   b . Property <Guid> ( "File" ) .
										   HasColumnType ( "uniqueidentifier" ) ;

									   b . Property <bool> ( "IsEncrypted" ) .
										   HasColumnType ( "bit" ) ;

									   b . Property <string> ( "RemoteFileName" ) .
										   HasColumnType ( "nvarchar(max)" ) ;

									   b . HasKey ( "Guid" ) ;

									   b . ToTable ( "BlockMetadata" ) ;
								   } ) ;

			modelBuilder . Entity (
								   "DreamRecorder.CloudFileSystem.FileSystem.FileMetadata" ,
								   b =>
								   {
									   b . Property <Guid> ( "Guid" ) .
										   ValueGeneratedOnAdd ( ) .
										   HasColumnType ( "uniqueidentifier" ) ;

									   b . Property <int> ( "AllocatedBlockCount" ) .
										   HasColumnType ( "int" ) ;

									   b . Property <long> ( "Attributes" ) .
										   HasColumnType ( "bigint" ) ;

									   b . Property <decimal> ( "ChangeTime" ) .
										   HasColumnType ( "decimal(20,0)" ) ;

									   b . Property <decimal> ( "CreationTime" ) .
										   HasColumnType ( "decimal(20,0)" ) ;

									   b . Property <long> ( "HardLinks" ) .
										   HasColumnType ( "bigint" ) ;

									   b . Property <decimal> ( "IndexNumber" ) .
										   HasColumnType ( "decimal(20,0)" ) ;

									   b . Property <bool> ( "IsDeleted" ) .
										   HasColumnType ( "bit" ) ;

									   b . Property <decimal> ( "LastAccessTime" ) .
										   HasColumnType ( "decimal(20,0)" ) ;

									   b . Property <decimal> ( "LastWriteTime" ) .
										   HasColumnType ( "decimal(20,0)" ) ;

									   b . Property <string> ( "Name" ) .
										   IsRequired ( ) .
										   HasColumnType ( "nvarchar(max)" ) ;

									   b . Property <long> ( "ReparseTag" ) .
										   HasColumnType ( "bigint" ) ;

									   b . Property <byte [ ]> ( "SecurityInfo" ) .
										   IsRequired ( ) .
										   HasColumnType ( "varbinary(max)" ) ;

									   b . Property <long> ( "Size" ) . HasColumnType ( "bigint" ) ;

									   b . HasKey ( "Guid" ) ;

									   b . ToTable ( "FileMetadata" ) ;
								   } ) ;
#pragma warning restore 612, 618
		}

	}

}
