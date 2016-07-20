//
// Author:
//   Jb Evain (jbevain@gmail.com)
//
// Copyright (c) 2008 - 2015 Jb Evain
// Copyright (c) 2008 - 2011 Novell, Inc.
//
// Licensed under the MIT/X11 license.
//

using System;
using System.Collections.Generic;
using System.IO;

using Mono.Cecil.Cil;

namespace Mono.Cecil.Pdb {

	class PdbHelper {

#if !READ_ONLY
		public static SymWriter CreateWriter (ModuleDefinition module, string pdb)
		{
			var writer = new SymWriter ();

			if (File.Exists (pdb))
				File.Delete (pdb);

			writer.Initialize (new ModuleMetadata (module), pdb, true);

			return writer;
		}
#endif
	}

	public class PdbReaderProvider : ISymbolReaderProvider {

		public ISymbolReader GetSymbolReader (ModuleDefinition module, string fileName)
		{
			Mixin.CheckModule (module);

			return new PdbReader (File.OpenRead (Mixin.GetPdbFileName (fileName)));
		}

		public ISymbolReader GetSymbolReader (ModuleDefinition module, Stream symbolStream)
		{
			Mixin.CheckModule (module);
			Mixin.CheckStream (symbolStream);

			return new PdbReader (symbolStream);
		}
	}

#if !READ_ONLY

	public class PdbWriterProvider : ISymbolWriterProvider {

		public ISymbolWriter GetSymbolWriter (ModuleDefinition module, string fileName)
		{
			return new PdbWriter (module, PdbHelper.CreateWriter (module, Mixin.GetPdbFileName (fileName)));
		}

		public ISymbolWriter GetSymbolWriter (ModuleDefinition module, Stream symbolStream)
		{
			throw new NotImplementedException ();
		}
	}

#endif
}

#if !NET_3_5 && !NET_4_0

namespace System.Runtime.CompilerServices {

	[AttributeUsage (AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Assembly)]
	sealed class ExtensionAttribute : Attribute {
	}
}

#endif
