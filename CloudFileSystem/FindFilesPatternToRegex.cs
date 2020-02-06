﻿using System ;
using System . Collections ;
using System . Collections . Generic ;
using System . Linq ;
using System . Text . RegularExpressions ;

namespace DreamRecorder . CloudFileSystem
{

	public static class FindFilesPatternToRegex
	{

		private static readonly Regex HasQuestionMarkRegEx =
			new Regex ( @"\?" , RegexOptions . Compiled ) ;

		private static readonly Regex IllegalCharactersRegex =
			new Regex ( "[" + @"\/:<>|" + "\"]" , RegexOptions . Compiled ) ;

		private static readonly Regex CatchExtensionRegex =
			new Regex ( @"^\s*.+\.([^\.]+)\s*$" , RegexOptions . Compiled ) ;

		private static string NonDotCharacters = @"[^.]*" ;

		public static Regex Convert ( string pattern )
		{
			if ( pattern == null )
			{
				throw new ArgumentNullException ( ) ;
			}

			pattern = pattern . Trim ( ) ;
			if ( pattern . Length == 0 )
			{
				throw new ArgumentException ( "Pattern is empty." ) ;
			}

			if ( IllegalCharactersRegex . IsMatch ( pattern ) )
			{
				throw new ArgumentException ( "Pattern contains illegal characters." ) ;
			}

			bool hasExtension = CatchExtensionRegex . IsMatch ( pattern ) ;
			bool matchExact   = false ;
			if ( HasQuestionMarkRegEx . IsMatch ( pattern ) )
			{
				matchExact = true ;
			}
			else if ( hasExtension )
			{
				matchExact = CatchExtensionRegex . Match ( pattern ) . Groups [ 1 ] . Length != 3 ;
			}

			string regexString = Regex . Escape ( pattern ) ;
			regexString = "^" + Regex . Replace ( regexString , @"\\\*" , ".*" ) ;
			regexString = Regex . Replace ( regexString , @"\\\?" , "." ) ;
			if ( ! matchExact && hasExtension )
			{
				regexString += NonDotCharacters ;
			}

			regexString += "$" ;
			Regex regex = new Regex (
									 regexString ,
									 RegexOptions . Compiled | RegexOptions . IgnoreCase ) ;
			return regex ;
		}

	}

}
