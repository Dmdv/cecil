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

using Mono.Collections.Generic;

using Mono.Cecil.Metadata;
using Mono.Cecil.PE;

namespace Mono.Cecil.Cil {

	public sealed class DefaultSymbolReaderProvider : ISymbolReaderProvider {

		public ISymbolReader GetSymbolReader (ModuleDefinition module, string fileName)
		{
			Mixin.CheckModule (module);
			Mixin.CheckFileName (fileName);

			var pdb_file = Mixin.GetPdbFileName (fileName);
			if (File.Exists (pdb_file) && IsPortablePdb (pdb_file))
				return new PortablePdbReaderProvider ().GetSymbolReader (module, fileName);

#if !PCL
			try {
				var platform_reader_provider = SymbolProvider.GetPlatformReaderProvider ();
				if (platform_reader_provider != null)
					return platform_reader_provider.GetSymbolReader (module, fileName);
			} catch (Exception) {
				return null;
			}
#endif
			return null;

		}

		static bool IsPortablePdb (string pdbFile)
		{
			using (var file = new FileStream (pdbFile, FileMode.Open, FileAccess.Read)) {
				var reader = new BinaryReader (file);
				return reader.ReadUInt32 () == 0x424a5342;
			}
		}

		public ISymbolReader GetSymbolReader (ModuleDefinition module, Stream symbolStream)
		{
			Mixin.CheckModule (module);
			Mixin.CheckStream (symbolStream);

			return new PortablePdbReaderProvider ().GetSymbolReader (module, symbolStream);
		}
	}

	public sealed class PortablePdbReaderProvider : ISymbolReaderProvider {

		public ISymbolReader GetSymbolReader (ModuleDefinition module, string fileName)
		{
			Mixin.CheckModule (module);
			Mixin.CheckFileName (fileName);

			using (var file = File.OpenRead (Mixin.GetPdbFileName (fileName)))
				return GetSymbolReader (module, file, file.Name);
		}

		public ISymbolReader GetSymbolReader (ModuleDefinition module, Stream symbolStream)
		{
			Mixin.CheckModule (module);
			Mixin.CheckStream (symbolStream);

			return GetSymbolReader (module, symbolStream, "");
		}

		ISymbolReader GetSymbolReader (ModuleDefinition module, Stream symbolStream, string fileName)
		{
			return new PortablePdbReader (ImageReader.ReadPortablePdb (symbolStream, fileName), module);
		}
	}

	public sealed class PortablePdbReader : ISymbolReader {

		readonly Image image;
		readonly ModuleDefinition module;
		readonly MetadataReader reader;
		readonly MetadataReader debug_reader;

		bool IsEmbedded { get { return reader.image == debug_reader.image; } }

		internal PortablePdbReader (Image image, ModuleDefinition module)
		{
			this.image = image;
			this.module = module;
			this.reader = module.reader;
			this.debug_reader = new MetadataReader (image, module, this.reader);
		}

		public bool ProcessDebugHeader (ImageDebugDirectory directory, byte [] header)
		{
			if (image == module.Image)
				return true;

			if (header.Length < 24)
				return false;

			var magic = ReadInt32 (header, 0);
			if (magic != 0x53445352)
				return false;

			var buffer = new byte [16];
			Buffer.BlockCopy (header, 4, buffer, 0, 16);

			var module_guid = new Guid (buffer);

			Buffer.BlockCopy (image.PdbHeap.Id, 0, buffer, 0, 16);

			var pdb_guid = new Guid (buffer);

			return module_guid == pdb_guid;
		}

		static int ReadInt32 (byte [] bytes, int start)
		{
			return (bytes [start]
				| (bytes [start + 1] << 8)
				| (bytes [start + 2] << 16)
				| (bytes [start + 3] << 24));
		}

		public MethodDebugInformation Read (MethodDefinition method)
		{
			var info = new MethodDebugInformation (method);
			ReadSequencePoints (info);
			ReadScope (info);
			ReadStateMachineKickOffMethod (info);
			ReadCustomDebugInformations (info);
			return info;
		}

		void ReadSequencePoints (MethodDebugInformation method_info)
		{
			method_info.sequence_points = debug_reader.ReadSequencePoints (method_info.method);
		}

		void ReadScope (MethodDebugInformation method_info)
		{
			method_info.scope = debug_reader.ReadScope (method_info.method);
		}

		void ReadStateMachineKickOffMethod (MethodDebugInformation method_info)
		{
			method_info.kickoff_method = debug_reader.ReadStateMachineKickoffMethod (method_info.method);
		}

		void ReadCustomDebugInformations (MethodDebugInformation info)
		{
			info.method.custom_infos = debug_reader.GetCustomDebugInformation (info.method);
		}

		public void Dispose ()
		{
			if (IsEmbedded)
				return;

			image.Dispose ();
		}
	}

#if !READ_ONLY

	public sealed class DefaultSymbolWriterProvider : ISymbolWriterProvider {

		public ISymbolWriter GetSymbolWriter (ModuleDefinition module, string fileName)
		{
			Mixin.CheckModule (module);
			Mixin.CheckFileName (fileName);

#if !PCL
			if (module.SymbolReader != null && !(module.SymbolReader is PortablePdbReader)) {
				var platform_writer_provider = SymbolProvider.GetPlatformWriterProvider ();
				if (platform_writer_provider != null)
					return platform_writer_provider.GetSymbolWriter (module, fileName);
			}

#endif
			return new PortablePdbWriterProvider ().GetSymbolWriter (module, fileName);
		}

		public ISymbolWriter GetSymbolWriter (ModuleDefinition module, Stream symbolStream)
		{
			Mixin.CheckModule (module);
			Mixin.CheckStream (symbolStream);

			return new PortablePdbWriterProvider ().GetSymbolWriter (module, symbolStream);
		}
	}

	public sealed class PortablePdbWriterProvider : ISymbolWriterProvider
	{
		public ISymbolWriter GetSymbolWriter (ModuleDefinition module, string fileName)
		{
			Mixin.CheckModule (module);
			Mixin.CheckFileName (fileName);

			return GetSymbolWriter (module, File.OpenWrite (Mixin.GetPdbFileName (fileName)));
		}

		public ISymbolWriter GetSymbolWriter (ModuleDefinition module, Stream symbolStream)
		{
			Mixin.CheckModule (module);
			Mixin.CheckStream (symbolStream);

			var metadata = new MetadataBuilder (module, this);
			var writer = ImageWriter.CreateDebugWriter (module, metadata, symbolStream);

			return new PortablePdbWriter (metadata, module, writer);
		}
	}

	sealed class PortablePdbWriter : ISymbolWriter {

		readonly MetadataBuilder pdb_metadata;
		readonly ModuleDefinition module;
		readonly ImageWriter writer;

		MetadataBuilder module_metadata;

		bool IsEmbedded { get { return writer == null; } }

		public PortablePdbWriter (MetadataBuilder pdb_metadata, ModuleDefinition module)
		{
			this.pdb_metadata = pdb_metadata;
			this.module = module;
		}

		public PortablePdbWriter (MetadataBuilder pdb_metadata, ModuleDefinition module, ImageWriter writer)
			: this (pdb_metadata, module)
		{
			this.writer = writer;
		}

		public void SetModuleMetadata (MetadataBuilder metadata)
		{
			this.module_metadata = metadata;

			if (module_metadata != pdb_metadata)
				this.pdb_metadata.metadata_builder = metadata;
		}

		public bool GetDebugHeader (out ImageDebugDirectory directory, out byte [] header)
		{
			if (IsEmbedded) {
				directory = new ImageDebugDirectory ();
				header = Empty<byte>.Array;
				return false;
			}

			directory = new ImageDebugDirectory () {
				MajorVersion = 256,
				MinorVersion = 20577,
				Type = 2,
			};

			var buffer = new ByteBuffer ();
			// RSDS
			buffer.WriteUInt32 (0x53445352);
			// Module ID
			buffer.WriteBytes (module.Mvid.ToByteArray ());
			// PDB Age
			buffer.WriteUInt32 (1);
			// PDB Path
			buffer.WriteBytes (System.Text.Encoding.UTF8.GetBytes (writer.BaseStream.GetFileName ()));
			buffer.WriteByte (0);

			header = new byte [buffer.length];
			Buffer.BlockCopy (buffer.buffer, 0, header, 0, buffer.length);
			directory.SizeOfData = header.Length;
			return true;
		}

		public void Write (MethodDebugInformation info)
		{
			CheckMethodDebugInformationTable ();

			pdb_metadata.AddMethodDebugInformation (info);
		}

		void CheckMethodDebugInformationTable ()
		{
			var mdi = pdb_metadata.table_heap.GetTable<MethodDebugInformationTable> (Table.MethodDebugInformation);
			if (mdi.length > 0)
				return;

			// The MethodDebugInformation table has the same length as the Method table
			mdi.rows = new Row<uint, uint> [module_metadata.method_rid - 1];
			mdi.length = mdi.rows.Length;
		}

		public void Dispose ()
		{
			if (IsEmbedded)
				return;

			WritePdbHeap ();
			WriteTableHeap ();

			writer.BuildMetadataTextMap ();
			writer.WriteMetadataHeader ();
			writer.WriteMetadata ();
#if NET_4_0
			writer.Dispose ();
#else
			writer.Close ();
#endif
		}

		void WritePdbHeap ()
		{
			var pdb_heap = pdb_metadata.pdb_heap;

			pdb_heap.WriteBytes (module.Mvid.ToByteArray ());
			pdb_heap.WriteUInt32 (module_metadata.time_stamp);

			pdb_heap.WriteUInt32 (module_metadata.entry_point.ToUInt32 ());

			var table_heap = module_metadata.table_heap;
			var tables = table_heap.tables;

			ulong valid = 0;
			for (int i = 0; i < tables.Length; i++) {
				if (tables [i] == null || tables [i].Length == 0)
					continue;

				valid |= (1UL << i);
			}

			pdb_heap.WriteUInt64 (valid);

			for (int i = 0; i < tables.Length; i++) {
				if (tables [i] == null || tables [i].Length == 0)
					continue;

				pdb_heap.WriteUInt32 ((uint) tables [i].Length);
			}
		}

		void WriteTableHeap ()
		{
			pdb_metadata.table_heap.WriteTableHeap ();
		}
	}

#endif

	static class PdbGuidMapping {

		static readonly Dictionary<Guid, DocumentLanguage> guid_language = new Dictionary<Guid, DocumentLanguage> ();
		static readonly Dictionary<DocumentLanguage, Guid> language_guid = new Dictionary<DocumentLanguage, Guid> ();

		static PdbGuidMapping ()
		{
			AddMapping (DocumentLanguage.C, new Guid (0x63a08714, 0xfc37, 0x11d2, 0x90, 0x4c, 0x0, 0xc0, 0x4f, 0xa3, 0x02, 0xa1));
			AddMapping (DocumentLanguage.Cpp, new Guid (0x3a12d0b7, 0xc26c, 0x11d0, 0xb4, 0x42, 0x0, 0xa0, 0x24, 0x4a, 0x1d, 0xd2));
			AddMapping (DocumentLanguage.CSharp, new Guid (0x3f5162f8, 0x07c6, 0x11d3, 0x90, 0x53, 0x0, 0xc0, 0x4f, 0xa3, 0x02, 0xa1));
			AddMapping (DocumentLanguage.Basic, new Guid (0x3a12d0b8, 0xc26c, 0x11d0, 0xb4, 0x42, 0x0, 0xa0, 0x24, 0x4a, 0x1d, 0xd2));
			AddMapping (DocumentLanguage.Java, new Guid (0x3a12d0b4, 0xc26c, 0x11d0, 0xb4, 0x42, 0x0, 0xa0, 0x24, 0x4a, 0x1d, 0xd2));
			AddMapping (DocumentLanguage.Cobol, new Guid (0xaf046cd1, 0xd0e1, 0x11d2, 0x97, 0x7c, 0x0, 0xa0, 0xc9, 0xb4, 0xd5, 0xc));
			AddMapping (DocumentLanguage.Pascal, new Guid (0xaf046cd2, 0xd0e1, 0x11d2, 0x97, 0x7c, 0x0, 0xa0, 0xc9, 0xb4, 0xd5, 0xc));
			AddMapping (DocumentLanguage.Cil, new Guid (0xaf046cd3, 0xd0e1, 0x11d2, 0x97, 0x7c, 0x0, 0xa0, 0xc9, 0xb4, 0xd5, 0xc));
			AddMapping (DocumentLanguage.JScript, new Guid (0x3a12d0b6, 0xc26c, 0x11d0, 0xb4, 0x42, 0x0, 0xa0, 0x24, 0x4a, 0x1d, 0xd2));
			AddMapping (DocumentLanguage.Smc, new Guid (0xd9b9f7b, 0x6611, 0x11d3, 0xbd, 0x2a, 0x0, 0x0, 0xf8, 0x8, 0x49, 0xbd));
			AddMapping (DocumentLanguage.MCpp, new Guid (0x4b35fde8, 0x07c6, 0x11d3, 0x90, 0x53, 0x0, 0xc0, 0x4f, 0xa3, 0x02, 0xa1));
			AddMapping (DocumentLanguage.FSharp, new Guid (0xab4f38c9, 0xb6e6, 0x43ba, 0xbe, 0x3b, 0x58, 0x08, 0x0b, 0x2c, 0xcc, 0xe3));
		}

		static void AddMapping (DocumentLanguage language, Guid guid)
		{
			guid_language.Add (guid, language);
			language_guid.Add (language, guid);
		}

		static readonly Guid type_text = new Guid (0x5a869d0b, 0x6611, 0x11d3, 0xbd, 0x2a, 0x00, 0x00, 0xf8, 0x08, 0x49, 0xbd);

		public static DocumentType ToType (this Guid guid)
		{
			if (guid == type_text)
				return DocumentType.Text;

			return DocumentType.Other;
		}

		public static Guid ToGuid (this DocumentType type)
		{
			if (type == DocumentType.Text)
				return type_text;

			return new Guid ();
		}

		static readonly Guid hash_md5 = new Guid (0x406ea660, 0x64cf, 0x4c82, 0xb6, 0xf0, 0x42, 0xd4, 0x81, 0x72, 0xa7, 0x99);
		static readonly Guid hash_sha1 = new Guid (0xff1816ec, 0xaa5e, 0x4d10, 0x87, 0xf7, 0x6f, 0x49, 0x63, 0x83, 0x34, 0x60);
		static readonly Guid hash_sha256 = new Guid (0x8829d00f, 0x11b8, 0x4213, 0x87, 0x8b, 0x77, 0x0e, 0x85, 0x97, 0xac, 0x16);

		public static DocumentHashAlgorithm ToHashAlgorithm (this Guid guid)
		{
			if (guid == hash_md5)
				return DocumentHashAlgorithm.MD5;

			if (guid == hash_sha1)
				return DocumentHashAlgorithm.SHA1;

			if (guid == hash_sha256)
				return DocumentHashAlgorithm.SHA256;

			return DocumentHashAlgorithm.None;
		}

		public static Guid ToGuid (this DocumentHashAlgorithm hash_algo)
		{
			if (hash_algo == DocumentHashAlgorithm.MD5)
				return hash_md5;

			if (hash_algo == DocumentHashAlgorithm.SHA1)
				return hash_sha1;

			return new Guid ();
		}

		public static DocumentLanguage ToLanguage (this Guid guid)
		{
			DocumentLanguage language;
			if (!guid_language.TryGetValue (guid, out language))
				return DocumentLanguage.Other;

			return language;
		}

		public static Guid ToGuid (this DocumentLanguage language)
		{
			Guid guid;
			if (!language_guid.TryGetValue (language, out guid))
				return new Guid ();

			return guid;
		}

		static readonly Guid vendor_ms = new Guid (0x994b45c4, 0xe6e9, 0x11d2, 0x90, 0x3f, 0x00, 0xc0, 0x4f, 0xa3, 0x02, 0xa1);

		public static DocumentLanguageVendor ToVendor (this Guid guid)
		{
			if (guid == vendor_ms)
				return DocumentLanguageVendor.Microsoft;

			return DocumentLanguageVendor.Other;
		}

		public static Guid ToGuid (this DocumentLanguageVendor vendor)
		{
			if (vendor == DocumentLanguageVendor.Microsoft)
				return vendor_ms;

			return new Guid ();
		}
	}
}
