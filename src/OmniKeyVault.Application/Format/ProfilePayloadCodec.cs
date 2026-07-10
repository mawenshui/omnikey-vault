using System.IO;
using System.Text;
using OmniKeyVault.Domain;

namespace OmniKeyVault.Application;

/// <summary>
/// Serializes and deserializes a profile's body (entries + folders + tags + templates)
/// per OKV_FORMAT.md §5. The output is plaintext bytes that are then AEAD-encrypted
/// by the VaultService with the profile's DEK.
///
/// Layout (matches OKV_FORMAT.md §5.3-§5.6):
///   Folders:        count(u4) + [ id(16) + name(var) + parent(16) ]*
///   Tags:           count(u4) + [ name(var) ]*
///   Templates:      count(u4) + [ id(var) + platform_id(var) + field_count(u4) + fields ]*
///   Entries:        count(u4) + [ header(id+ver+type+updated) + payload-plaintext ]*
/// </summary>
public sealed class ProfilePayloadCodec
{
    public byte[] Encode(IReadOnlyList<Entry> entries, IReadOnlyList<Folder> folders, IReadOnlyList<string> tags, IReadOnlyList<Template> templates)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: false);

        // Folders (§5.3)
        bw.Write((uint)folders.Count);
        foreach (var f in folders)
        {
            bw.Write(f.Id.ToByteArray());
            WriteString(bw, f.Name);
            bw.Write(f.ParentId.HasValue ? f.ParentId.Value.ToByteArray() : new byte[16]);
        }

        // Tags (§5.4)
        bw.Write((uint)tags.Count);
        foreach (var t in tags)
            WriteString(bw, t);

        // Templates (§5.5) — minimal storage form
        bw.Write((uint)templates.Count);
        foreach (var tpl in templates)
        {
            WriteString(bw, tpl.Id);
            WriteString(bw, tpl.PlatformId);
            bw.Write((uint)tpl.Fields.Count);
            foreach (var tf in tpl.Fields)
            {
                WriteString(bw, tf.Key);
                bw.Write((byte)tf.Kind);
                bw.Write(tf.Sensitive ? (byte)1 : (byte)0);
                bw.Write(tf.Required ? (byte)1 : (byte)0);
                WriteString(bw, tf.DefaultMask ?? string.Empty);
                bw.Write(tf.Validation != null ? (byte)1 : (byte)0);
                if (tf.Validation != null)
                {
                    WriteString(bw, tf.Validation.Regex ?? string.Empty);
                    WriteString(bw, tf.Validation.Hint ?? string.Empty);
                }
            }
        }

        // Entries (§5.6) — header + payload, all plaintext (the entire section is then AEAD-encrypted)
        bw.Write((uint)entries.Count);
        foreach (var e in entries)
        {
            // Header
            bw.Write(e.Id.ToByteArray());
            bw.Write(e.Version);
            bw.Write((byte)e.Type);
            bw.Write(e.UpdatedAt.ToUnixTimeMilliseconds());
            // Payload (plaintext)
            WriteString(bw, e.Name);
            WriteString(bw, e.PlatformId ?? string.Empty);
            bw.Write((uint)e.Tags.Count);
            foreach (var t in e.Tags) WriteString(bw, t);
            bw.Write(e.Folder.HasValue ? e.Folder.Value.ToByteArray() : new byte[16]);
            WriteString(bw, e.Notes ?? string.Empty);
            bw.Write(e.CreatedAt.ToUnixTimeMilliseconds());
            bw.Write(e.ExpiresAt.HasValue ? e.ExpiresAt.Value.ToUnixTimeMilliseconds() : -1L);
            bw.Write((uint)e.Fields.Count);
            foreach (var fld in e.Fields)
            {
                WriteString(bw, fld.Key);
                bw.Write((byte)fld.Kind);
                bw.Write(fld.Sensitive ? (byte)1 : (byte)0);
                WriteBytes(bw, fld.Value);
                WriteString(bw, fld.Mask ?? string.Empty);
                bw.Write(fld.Validation != null ? (byte)1 : (byte)0);
                if (fld.Validation != null)
                {
                    WriteString(bw, fld.Validation.Regex ?? string.Empty);
                    WriteString(bw, fld.Validation.Hint ?? string.Empty);
                }
            }
        }

        return ms.ToArray();
    }

    public (IReadOnlyList<Entry> Entries, IReadOnlyList<Folder> Folders, IReadOnlyList<string> Tags, IReadOnlyList<Template> Templates) Decode(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: false);

        // Folders
        var folders = new List<Folder>();
        var folderCount = br.ReadUInt32();
        for (uint i = 0; i < folderCount; i++)
        {
            var id = new Guid(br.ReadBytes(16));
            var name = ReadString(br);
            var parentBytes = br.ReadBytes(16);
            Guid? parentId = parentBytes.All(b => b == 0) ? null : new Guid(parentBytes);
            folders.Add(new Folder { Id = id, Name = name, ParentId = parentId });
        }

        // Tags
        var tags = new List<string>();
        var tagCount = br.ReadUInt32();
        for (uint i = 0; i < tagCount; i++)
            tags.Add(ReadString(br));

        // Templates
        var templates = new List<Template>();
        var tplCount = br.ReadUInt32();
        for (uint i = 0; i < tplCount; i++)
        {
            var id = ReadString(br);
            var pid = ReadString(br);
            var fc = br.ReadUInt32();
            var fields = new List<TemplateField>();
            for (uint j = 0; j < fc; j++)
            {
                var key = ReadString(br);
                var kind = (FieldKind)br.ReadByte();
                var sens = br.ReadByte() != 0;
                var req = br.ReadByte() != 0;
                var mask = ReadString(br);
                var hasVal = br.ReadByte() != 0;
                FieldValidation? val = null;
                if (hasVal)
                {
                    var rx = ReadString(br);
                    var hint = ReadString(br);
                    val = new FieldValidation { Regex = string.IsNullOrEmpty(rx) ? null : rx, Hint = string.IsNullOrEmpty(hint) ? null : hint };
                }
                fields.Add(new TemplateField { Key = key, Kind = kind, Sensitive = sens, Required = req, DefaultMask = string.IsNullOrEmpty(mask) ? null : mask, Validation = val });
            }
            templates.Add(new Template { Id = id, PlatformId = pid, Fields = fields });
        }

        // Entries
        var entries = new List<Entry>();
        var entryCount = br.ReadUInt32();
        for (uint i = 0; i < entryCount; i++)
        {
            // Header
            var id = new Guid(br.ReadBytes(16));
            var version = br.ReadUInt32();
            var type = (EntryType)br.ReadByte();
            var updatedAt = DateTimeOffset.FromUnixTimeMilliseconds(br.ReadInt64());

            // Payload
            var name = ReadString(br);
            var pidStr = ReadString(br);
            var tagC = br.ReadUInt32();
            var entryTags = new List<string>();
            for (uint j = 0; j < tagC; j++) entryTags.Add(ReadString(br));
            var folderBytes = br.ReadBytes(16);
            Guid? folderId = folderBytes.All(b => b == 0) ? null : new Guid(folderBytes);
            var notes = ReadString(br);
            var createdAt = DateTimeOffset.FromUnixTimeMilliseconds(br.ReadInt64());
            var expiresRaw = br.ReadInt64();
            DateTimeOffset? expiresAt = expiresRaw == -1 ? null : DateTimeOffset.FromUnixTimeMilliseconds(expiresRaw);
            var fc = br.ReadUInt32();
            var fields = new List<Field>();
            for (uint j = 0; j < fc; j++)
            {
                var key = ReadString(br);
                var kind = (FieldKind)br.ReadByte();
                var sens = br.ReadByte() != 0;
                var value = ReadBytes(br);
                var mask = ReadString(br);
                var hasVal = br.ReadByte() != 0;
                FieldValidation? val = null;
                if (hasVal)
                {
                    var rx = ReadString(br);
                    var hint = ReadString(br);
                    val = new FieldValidation { Regex = string.IsNullOrEmpty(rx) ? null : rx, Hint = string.IsNullOrEmpty(hint) ? null : hint };
                }
                fields.Add(new Field { Key = key, Value = value, Kind = kind, Sensitive = sens, Mask = string.IsNullOrEmpty(mask) ? null : mask, Validation = val });
            }

            entries.Add(new Entry
            {
                Id = id,
                Type = type,
                Name = name,
                PlatformId = string.IsNullOrEmpty(pidStr) ? null : pidStr,
                Tags = entryTags,
                Folder = folderId,
                Fields = fields,
                Notes = string.IsNullOrEmpty(notes) ? null : notes,
                CreatedAt = createdAt,
                UpdatedAt = updatedAt,
                ExpiresAt = expiresAt,
                Version = version
            });
        }

        return (entries, folders, tags, templates);
    }

    private static void WriteString(BinaryWriter bw, string s)
    {
        var b = Encoding.UTF8.GetBytes(s);
        bw.Write((uint)b.Length);
        bw.Write(b);
    }

    // P6-T3: Write/Read raw bytes for Field.Value (byte[] instead of string).
    // Binary format is identical to WriteString/ReadString (length-prefixed bytes),
    // so existing vault files decode correctly.
    private static void WriteBytes(BinaryWriter bw, byte[] b)
    {
        bw.Write((uint)b.Length);
        bw.Write(b);
    }

    private static byte[] ReadBytes(BinaryReader br)
    {
        var len = br.ReadUInt32();
        if (len > 16 * 1024 * 1024) throw new FileCorruptException($"Byte array length {len} exceeds 16 MiB limit.");
        return br.ReadBytes((int)len);
    }

    private static string ReadString(BinaryReader br)
    {
        var len = br.ReadUInt32();
        if (len > 16 * 1024 * 1024) throw new FileCorruptException($"String length {len} exceeds 16 MiB limit.");
        var b = br.ReadBytes((int)len);
        return Encoding.UTF8.GetString(b);
    }
}
