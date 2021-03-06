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
using System.IO;

using Mono.Cecil.Cil;
using Mono.Cecil.Metadata;

using RVA = System.UInt32;

namespace Mono.Cecil.PE {

	sealed class Image : IDisposable {

		public Stream Stream;
		public string FileName;

		public ModuleKind Kind;
		public string RuntimeVersion;
		public TargetArchitecture Architecture;
		public ModuleCharacteristics Characteristics;

		public Section [] Sections;

		public Section MetadataSection;

		public uint EntryPointToken;
		public ModuleAttributes Attributes;

		public DataDirectory Debug;
		public DataDirectory Resources;
		public DataDirectory StrongName;

		public StringHeap StringHeap;
		public BlobHeap BlobHeap;
		public UserStringHeap UserStringHeap;
		public GuidHeap GuidHeap;
		public TableHeap TableHeap;
		public PdbHeap PdbHeap;

		readonly int [] coded_index_sizes = new int [14];

		readonly Func<Table, int> counter;

		public Image ()
		{
			counter = GetTableLength;
		}

		public bool HasTable (Table table)
		{
			return GetTableLength (table) > 0;
		}

		public int GetTableLength (Table table)
		{
			return (int) TableHeap [table].Length;
		}

		public int GetTableIndexSize (Table table)
		{
			return GetTableLength (table) < 65536 ? 2 : 4;
		}

		public int GetCodedIndexSize (CodedIndex coded_index)
		{
			var index = (int) coded_index;
			var size = coded_index_sizes [index];
			if (size != 0)
				return size;

			return coded_index_sizes [index] = coded_index.GetSize (counter);
		}

		public uint ResolveVirtualAddress (RVA rva)
		{
			var section = GetSectionAtVirtualAddress (rva);
			if (section == null)
				throw new ArgumentOutOfRangeException ();

			return ResolveVirtualAddressInSection (rva, section);
		}

		public uint ResolveVirtualAddressInSection (RVA rva, Section section)
		{
			return rva + section.PointerToRawData - section.VirtualAddress;
		}

		public Section GetSection (string name)
		{
			var sections = this.Sections;
			for (int i = 0; i < sections.Length; i++) {
				var section = sections [i];
				if (section.Name == name)
					return section;
			}

			return null;
		}

		public Section GetSectionAtVirtualAddress (RVA rva)
		{
			var sections = this.Sections;
			for (int i = 0; i < sections.Length; i++) {
				var section = sections [i];
				if (rva >= section.VirtualAddress && rva < section.VirtualAddress + section.SizeOfRawData)
					return section;
			}

			return null;
		}

		public BinaryStreamReader GetReaderAt (RVA rva)
		{
			var section = GetSectionAtVirtualAddress (rva);
			if (section == null)
				return null;

			var reader = new BinaryStreamReader (Stream);
			reader.MoveTo (ResolveVirtualAddressInSection (rva, section));
			return reader;
		}

		public ImageDebugDirectory GetDebugHeader (out byte [] header)
		{
			var reader = GetReaderAt (Debug.VirtualAddress);
			if (reader == null) {
				header = Empty<byte>.Array;
				return new ImageDebugDirectory ();
			}

			var directory = new ImageDebugDirectory {
				Characteristics = reader.ReadInt32 (),
				TimeDateStamp = reader.ReadInt32 (),
				MajorVersion = reader.ReadInt16 (),
				MinorVersion = reader.ReadInt16 (),
				Type = reader.ReadInt32 (),
				SizeOfData = reader.ReadInt32 (),
				AddressOfRawData = reader.ReadInt32 (),
				PointerToRawData = reader.ReadInt32 (),
			};

			reader = GetReaderAt ((uint) directory.AddressOfRawData);
			header = reader != null
				? reader.ReadBytes (directory.SizeOfData)
				: Empty<byte>.Array;

			return directory;
		}

		public bool HasDebugTables ()
		{
			return HasTable (Table.Document)
				|| HasTable (Table.MethodDebugInformation)
				|| HasTable (Table.LocalScope)
				|| HasTable (Table.LocalVariable)
				|| HasTable (Table.LocalConstant)
				|| HasTable (Table.StateMachineMethod)
				|| HasTable (Table.CustomDebugInformation);
		}

		public void Dispose ()
		{
			Stream.Dispose ();
		}
	}
}
