using Google.Protobuf;
using Google.Protobuf.Collections;
using Google.Protobuf.Reflection;
using System;
using System.Linq;

namespace GenShin_Launcher_Plus.Models.Sophon;

public sealed class SophonChunkManifest : IMessage<SophonChunkManifest>
{
    private static readonly MessageParser<SophonChunkManifest> _parser = new(() => new SophonChunkManifest());
    public static MessageParser<SophonChunkManifest> Parser => _parser;
    private readonly RepeatedField<SophonChunkFile> chuncks_ = new();
    public RepeatedField<SophonChunkFile> Chuncks => chuncks_;
    public void MergeFrom(SophonChunkManifest? other) { if (other != null) chuncks_.Add(other.chuncks_); }
    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            if ((tag & 7) == 2)
            {
                var item = new SophonChunkFile();
                input.ReadMessage(item);
                chuncks_.Add(item);
            }
            else input.SkipLastField();
        }
    }
    public void WriteTo(CodedOutputStream output) { foreach (var v in chuncks_) { output.WriteRawTag(10); output.WriteMessage(v); } }
    public int CalculateSize() { int s = 0; foreach (var v in chuncks_) s += 1 + CodedOutputStream.ComputeMessageSize(v); return s; }
    public SophonChunkManifest Clone() => new() { chuncks_ = { chuncks_ } };
    public MessageDescriptor Descriptor => throw new NotSupportedException("SophonChunkManifest is a lightweight manual protobuf model.");
    public bool Equals(SophonChunkManifest? other) => other != null && chuncks_.SequenceEqual(other.chuncks_);
    public override bool Equals(object? obj) => Equals(obj as SophonChunkManifest);
    public override int GetHashCode() => chuncks_.Aggregate(17, (hash, item) => HashCode.Combine(hash, item));
    public override string ToString() => $"{nameof(SophonChunkManifest)} {{ Chuncks = {chuncks_.Count} }}";
}

public sealed class SophonChunkFile : IMessage<SophonChunkFile>
{
    private static readonly MessageParser<SophonChunkFile> _parser = new(() => new SophonChunkFile());
    public static MessageParser<SophonChunkFile> Parser => _parser;
    private string file_ = "";
    private readonly RepeatedField<SophonChunk> chunks_ = new();
    private bool isFolder_;
    private long size_;
    private string md5_ = "";
    public string File { get => file_; set => file_ = value ?? ""; }
    public RepeatedField<SophonChunk> Chunks => chunks_;
    public bool IsFolder { get => isFolder_; set => isFolder_ = value; }
    public long Size { get => size_; set => size_ = value; }
    public string Md5 { get => md5_; set => md5_ = value ?? ""; }
    public void MergeFrom(SophonChunkFile? other) { if (other != null) { file_ = other.file_; chunks_.Add(other.chunks_); isFolder_ = other.isFolder_; size_ = other.size_; md5_ = other.md5_; } }
    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            switch (tag >> 3)
            {
                case 1: file_ = input.ReadString(); break;
                case 2:
                    var c = new SophonChunk();
                    input.ReadMessage(c);
                    chunks_.Add(c);
                    break;
                case 3: isFolder_ = input.ReadBool(); break;
                case 4: size_ = input.ReadInt64(); break;
                case 5: md5_ = input.ReadString(); break;
                default: input.SkipLastField(); break;
            }
        }
    }
    public void WriteTo(CodedOutputStream output) { if (file_.Length != 0) { output.WriteRawTag(10); output.WriteString(file_); } foreach (var v in chunks_) { output.WriteRawTag(18); output.WriteMessage(v); } if (isFolder_) { output.WriteRawTag(24); output.WriteBool(isFolder_); } if (size_ != 0) { output.WriteRawTag(32); output.WriteInt64(size_); } if (md5_.Length != 0) { output.WriteRawTag(42); output.WriteString(md5_); } }
    public int CalculateSize() { int s = 0; if (file_.Length != 0) s += 1 + CodedOutputStream.ComputeStringSize(file_); foreach (var v in chunks_) s += 1 + CodedOutputStream.ComputeMessageSize(v); if (isFolder_) s += 2; if (size_ != 0) s += 1 + CodedOutputStream.ComputeInt64Size(size_); if (md5_.Length != 0) s += 1 + CodedOutputStream.ComputeStringSize(md5_); return s; }
    public SophonChunkFile Clone() => new() { file_ = file_, chunks_ = { chunks_ }, isFolder_ = isFolder_, size_ = size_, md5_ = md5_ };
    public MessageDescriptor Descriptor => throw new NotSupportedException("SophonChunkFile is a lightweight manual protobuf model.");
    public bool Equals(SophonChunkFile? other) => other != null && file_ == other.file_ && chunks_.SequenceEqual(other.chunks_) && isFolder_ == other.isFolder_ && size_ == other.size_ && md5_ == other.md5_;
    public override bool Equals(object? obj) => Equals(obj as SophonChunkFile);
    public override int GetHashCode() => HashCode.Combine(file_, chunks_.Aggregate(17, (hash, item) => HashCode.Combine(hash, item)), isFolder_, size_, md5_);
    public override string ToString() => $"{nameof(SophonChunkFile)} {{ File = {file_}, Chunks = {chunks_.Count}, Size = {size_} }}";
}

public sealed class SophonChunk : IMessage<SophonChunk>
{
    private static readonly MessageParser<SophonChunk> _parser = new(() => new SophonChunk());
    public static MessageParser<SophonChunk> Parser => _parser;
    private string id_ = "";
    private string uncompressedMd5_ = "";
    private long offset_;
    private long compressedSize_;
    private long uncompressedSize_;
    private long unknown_;
    private string compressedMd5_ = "";
    public string Id { get => id_; set => id_ = value ?? ""; }
    public string UncompressedMd5 { get => uncompressedMd5_; set => uncompressedMd5_ = value ?? ""; }
    public long Offset { get => offset_; set => offset_ = value; }
    public long CompressedSize { get => compressedSize_; set => compressedSize_ = value; }
    public long UncompressedSize { get => uncompressedSize_; set => uncompressedSize_ = value; }
    public string CompressedMd5 { get => compressedMd5_; set => compressedMd5_ = value ?? ""; }
    public void MergeFrom(SophonChunk? other) { if (other != null) { id_ = other.id_; uncompressedMd5_ = other.uncompressedMd5_; offset_ = other.offset_; compressedSize_ = other.compressedSize_; uncompressedSize_ = other.uncompressedSize_; unknown_ = other.unknown_; compressedMd5_ = other.compressedMd5_; } }
    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            switch (tag >> 3)
            {
                case 1: id_ = input.ReadString(); break;
                case 2: uncompressedMd5_ = input.ReadString(); break;
                case 3: offset_ = input.ReadInt64(); break;
                case 4: compressedSize_ = input.ReadInt64(); break;
                case 5: uncompressedSize_ = input.ReadInt64(); break;
                case 6: unknown_ = input.ReadInt64(); break;
                case 7: compressedMd5_ = input.ReadString(); break;
                default: input.SkipLastField(); break;
            }
        }
    }
    public void WriteTo(CodedOutputStream output) { if (id_.Length != 0) { output.WriteRawTag(10); output.WriteString(id_); } if (uncompressedMd5_.Length != 0) { output.WriteRawTag(18); output.WriteString(uncompressedMd5_); } if (offset_ != 0) { output.WriteRawTag(24); output.WriteInt64(offset_); } if (compressedSize_ != 0) { output.WriteRawTag(32); output.WriteInt64(compressedSize_); } if (uncompressedSize_ != 0) { output.WriteRawTag(40); output.WriteInt64(uncompressedSize_); } if (unknown_ != 0) { output.WriteRawTag(48); output.WriteInt64(unknown_); } if (compressedMd5_.Length != 0) { output.WriteRawTag(58); output.WriteString(compressedMd5_); } }
    public int CalculateSize() { int s = 0; if (id_.Length != 0) s += 1 + CodedOutputStream.ComputeStringSize(id_); if (uncompressedMd5_.Length != 0) s += 1 + CodedOutputStream.ComputeStringSize(uncompressedMd5_); if (offset_ != 0) s += 1 + CodedOutputStream.ComputeInt64Size(offset_); if (compressedSize_ != 0) s += 1 + CodedOutputStream.ComputeInt64Size(compressedSize_); if (uncompressedSize_ != 0) s += 1 + CodedOutputStream.ComputeInt64Size(uncompressedSize_); if (unknown_ != 0) s += 1 + CodedOutputStream.ComputeInt64Size(unknown_); if (compressedMd5_.Length != 0) s += 1 + CodedOutputStream.ComputeStringSize(compressedMd5_); return s; }
    public SophonChunk Clone() => new() { id_ = id_, uncompressedMd5_ = uncompressedMd5_, offset_ = offset_, compressedSize_ = compressedSize_, uncompressedSize_ = uncompressedSize_, unknown_ = unknown_, compressedMd5_ = compressedMd5_ };
    public MessageDescriptor Descriptor => throw new NotSupportedException("SophonChunk is a lightweight manual protobuf model.");
    public bool Equals(SophonChunk? other) => other != null && id_ == other.id_ && uncompressedMd5_ == other.uncompressedMd5_ && offset_ == other.offset_ && compressedSize_ == other.compressedSize_ && uncompressedSize_ == other.uncompressedSize_ && unknown_ == other.unknown_ && compressedMd5_ == other.compressedMd5_;
    public override bool Equals(object? obj) => Equals(obj as SophonChunk);
    public override int GetHashCode() => HashCode.Combine(id_, uncompressedMd5_, offset_, compressedSize_, uncompressedSize_, unknown_, compressedMd5_);
    public override string ToString() => $"{nameof(SophonChunk)} {{ Id = {id_}, Offset = {offset_}, CompressedSize = {compressedSize_}, UncompressedSize = {uncompressedSize_} }}";
}
