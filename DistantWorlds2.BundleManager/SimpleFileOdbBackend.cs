using Xenko.Core.IO;
using Xenko.Core.Serialization.Contents;
using Xenko.Core.Storage;

namespace DistantWorlds2.BundleManager;

public class SimpleFileOdbBackend : IOdbBackend
{
    public SimpleFileOdbBackend(string prefix, IEnumerable<KeyValuePair<string, ObjectId>> map)
    {
        Prefix = prefix;
        var cim = Xenko.Core.Serialization.Contents.ContentIndexMap.CreateInMemory();
        cim.AddValues(map);
        ContentIndexMap = cim;
    }

    public string Prefix { get; }

    public void Dispose()
        => ContentIndexMap.Dispose();

    public Stream OpenStream(ObjectId objectId, VirtualFileMode mode = VirtualFileMode.Open, VirtualFileAccess access = VirtualFileAccess.Read,
        VirtualFileShare share = VirtualFileShare.Read)
        => new FileStream(GetFilePath(objectId), (FileMode)mode, (FileAccess)access, (FileShare)share);

    public int GetSize(ObjectId objectId)
        => checked((int)new FileInfo(GetFilePath(objectId)).Length);

    public ObjectId Write(ObjectId objectId, Stream dataStream, int length, bool forceWrite = false)
        => throw new NotImplementedException();

    public OdbStreamWriter CreateStream()
        => throw new NotImplementedException();

    public bool Exists(ObjectId objectId)
        => File.Exists(GetFilePath(objectId));

    public IEnumerable<ObjectId> EnumerateObjects()
        => ContentIndexMap.SearchValues(x => true).Select(x => x.Value);

    public void Delete(ObjectId objectId)
        => throw new NotImplementedException();

    public string GetFilePath(ObjectId objectId)
        => Path.Combine(Prefix, ContentIndexMap.SearchValues(x => x.Value == objectId).First().Key);

    public IContentIndexMap ContentIndexMap { get; }
}
