

using Xenko.Core;
using Xenko.Core.Serialization;
using Xenko.Core.Serialization.Contents;
using Xenko.Core.Streaming;
using Xenko.Graphics;

namespace DistantWorlds2.BundleManager
{
    internal class StreamingImage
    {
        public static Image Load(BinarySerializationReader bsr, string path)
        {
            ImageDescription imageDescription = new ImageDescription();
            SerializerSelector.Default.GetSerializer<ImageDescription>().Serialize(ref imageDescription, ArchiveMode.Deserialize, bsr);
            ContentStorageHeader storageHeader;
            ContentStorageHeader.Read(bsr, out storageHeader);

            using var ds = File.OpenRead(path + "_Data");
            int totalSize = CalculateSizeInBytes(imageDescription);

            byte[] data = new byte[totalSize];
            int destOffset = 0;
            using BinaryReader br = new BinaryReader(ds);
            unsafe
            {
                IntPtr dest = Utilities.AllocateMemory(totalSize);
                fixed (byte* src = data)
                {
                    for (int i = 0; i < storageHeader.ChunksCount; i++)
                    {
                        int width = Math.Max(1, imageDescription.Width >> i);
                        int height = Math.Max(1, imageDescription.Height >> i);

                        ds.Position = storageHeader.Chunks[i].Location;
                        ds.Read(data, 0, storageHeader.Chunks[i].Size);
                        Utilities.CopyMemory(dest + destOffset, (IntPtr)src, storageHeader.Chunks[i].Size);
                        destOffset += SlicePitch(width, height, imageDescription.Format);
                    }
                    return Image.New(imageDescription, (IntPtr)dest, 0, new System.Runtime.InteropServices.GCHandle?(), true);
                }
            }
        }        

        public static void Write(string root, string assetPath, Image image)
        {
            var outputPath = Path.Combine(root, assetPath);
            var dataOutputPath = outputPath + "_Data";

            Directory.CreateDirectory(Directory.GetParent(outputPath).FullName);

            using FileStream destStream = File.OpenWrite(outputPath);
            
            BinarySerializationWriter serializationWriter = new BinarySerializationWriter(destStream);
            ChunkHeader header = new ChunkHeader
            {
                Version = 1,
                Type = typeof(Texture).AssemblyQualifiedName
            };
            header.Write(serializationWriter);
            header.OffsetToReferences = (int)serializationWriter.NativeStream.Position;
            serializationWriter.Write(0); //No references in Textures as far as I can tell.
            header.OffsetToObject = (int)serializationWriter.NativeStream.Position;
            serializationWriter.Write<byte>(1);

            SerializerSelector.Default.GetSerializer<ImageDescription>().Serialize(image.Description, serializationWriter);
            ContentStorageHeader storageHeader = new ContentStorageHeader();

            storageHeader.InitialImage = false; //TODO: Use small mipmap as initial image.
            storageHeader.PackageTime = DateTime.UtcNow;
            storageHeader.DataUrl = assetPath + "_Data";
            storageHeader.Chunks = new ContentStorageHeader.ChunkEntry[image.Description.MipLevels];

            int writeOffset = 0;
            for (int i = 0; i < image.Description.MipLevels; i++)
            {
                int width = Math.Max(1, image.Description.Width >> i);
                int height = Math.Max(1, image.Description.Height >> i);
                int pitch = SlicePitch(width, height, image.Description.Format);
                storageHeader.Chunks[i] = new ContentStorageHeader.ChunkEntry
                {
                    Location = writeOffset,
                    Size = pitch
                };
                writeOffset += pitch;
            }
            storageHeader.HashCode = GetHashCode(storageHeader);
            storageHeader.Write(serializationWriter);
            serializationWriter.Write(0);
            destStream.Position = 0;
            header.Write(serializationWriter);

            using FileStream dataStream = new FileStream(dataOutputPath, FileMode.Create);            
            int totalSize = CalculateSizeInBytes(image.Description);
            dataStream.SetLength(totalSize);
            dataStream.Seek(0, SeekOrigin.Begin);
            unsafe
            {
                using (UnmanagedMemoryStream memoryStream = new UnmanagedMemoryStream((byte*)image.DataPointer, totalSize))
                {
                    memoryStream.CopyTo(dataStream);
                }
            }            
        }

        static private int CalculateSizeInBytes(ImageDescription desc)
        {
            bool isCompressed = desc.Format.IsCompressed();
            int blockSize = desc.Format.SizeInBytes();
            if (isCompressed)
            {
                switch (desc.Format)
                {
                    case PixelFormat.BC1_Typeless:
                    case PixelFormat.BC1_UNorm:
                    case PixelFormat.BC1_UNorm_SRgb:
                    case PixelFormat.BC4_Typeless:
                    case PixelFormat.BC4_UNorm:
                    case PixelFormat.BC4_SNorm:
                        blockSize = 8;
                        break;
                    default:
                        blockSize = 16;
                        break;
                }
            }
            int size = 0;
            for (int index = 0; index < desc.MipLevels; ++index)
            {
                int width = Math.Max(1, desc.Width >> index);
                int height = Math.Max(1, desc.Height >> index);
                if (isCompressed)
                {
                    width = (width + 3) / 4;
                    height = (height + 3) / 4;
                }
                int slicePitch = Math.Max(1, height) * Math.Max(1, width) * blockSize;
                size += slicePitch;
            }
            return size * desc.ArraySize;
        }

        static private int SlicePitch(int width, int height, PixelFormat format)
        {
            bool isCompressed = format.IsCompressed();
            int blockSize = format.SizeInBytes();
            if (isCompressed)
            {
                switch (format)
                {
                    case PixelFormat.BC1_Typeless:
                    case PixelFormat.BC1_UNorm:
                    case PixelFormat.BC1_UNorm_SRgb:
                    case PixelFormat.BC4_Typeless:
                    case PixelFormat.BC4_UNorm:
                    case PixelFormat.BC4_SNorm:
                        blockSize = 8;
                        break;
                    default:
                        blockSize = 16;
                        break;
                }
            }
            if (isCompressed)
            {
                width = (width + 3) / 4;
                height = (height + 3) / 4;
            }
            int slicePitch = Math.Max(1, height) * Math.Max(1, width) * blockSize;
            return slicePitch;
        }

        private static int GetHashCode(ContentStorageHeader header)
        {
            int hashCode = (int)header.PackageTime.Ticks * 397 ^ header.Chunks.Length;
            for (int index = 0; index < header.Chunks.Length; ++index)
                hashCode = hashCode * 397 ^ header.Chunks[index].Size;
            return hashCode;
        }
    }
}
