using Xenko.Audio;
using Xenko.Core.Serialization;
using Xenko.Core.Serialization.Contents;

using FFMpegCore;
using FFMpegCore.Helpers;

namespace DistantWorlds2.BundleManager
{
    public static class SoundConverter
    {
        const int SamplesPerFrame = 512;
        static string FFmpegPath = "";

        public static bool FindFFmpeg()
        {
            if (File.Exists("ffmpeg.exe") && File.Exists("ffprobe.exe"))
            {
                FFmpegPath = ".";
                return true;
            }
            foreach (var target in new[] { EnvironmentVariableTarget.Process, EnvironmentVariableTarget.User, EnvironmentVariableTarget.Machine })
            {
                var PATH = Environment.GetEnvironmentVariable("Path", target);
                foreach (var path in PATH.Split(';'))
                {
                    if (File.Exists(Path.Combine(path, "ffmpeg.exe")) && File.Exists(Path.Combine(path, "ffprobe.exe")))
                    {
                        FFmpegPath = path;
                        return true;
                    }
                }
            }
            
            return false;
        }
      
        public static void XenkoToSoundfile(string src, string dst)
        {
            if (src.EndsWith("_Data"))
                return;
            if(FFmpegPath == string.Empty)
            {
                if(!FindFFmpeg())
                {
                    Console.Error.WriteLine("Error: Failed to find FFmpeg.");
                    throw new NotSupportedException("Failed to find FFmpeg.");
                }
            }
            string dataSrc = src + "_Data";

            using var srcFs = File.OpenRead(src);
            var bsr = new BinarySerializationReader(srcFs);
            var chunkHeader = ChunkHeader.Read(bsr);
            if (chunkHeader == null || !chunkHeader.Type.Contains("Xenko.Audio.Sound"))
            {
                Console.WriteLine($"{src} is not a Sound, skipping");
                return;
            }
                
            srcFs.Seek(chunkHeader.OffsetToObject+1, SeekOrigin.Begin);

            string compressedDataUrl = bsr.ReadString();
            int sampleRate = bsr.ReadInt32();
            int channels = bsr.ReadByte();
            /*bool StreamFromDisk = */bsr.ReadBoolean();
            /*bool Spatialized = */bsr.ReadBoolean();
            int numberOfPackets = bsr.ReadInt32();
            int maxPacketLength = bsr.ReadInt16();
            int samples = bsr.ReadInt32();

            using var celt = new Celt(sampleRate, SamplesPerFrame, channels, true);
            using var dataStream = File.OpenRead(dataSrc);
            var dataReader = new BinarySerializationReader(dataStream);
            byte[] inputSamples = new byte[maxPacketLength];
            float[] outputSamples = new float[512*channels];

            var tempFile = Path.GetTempFileName();
            using var tempStream = File.Create(tempFile);
            var tempWriter = new BinaryWriter(tempStream);

            for(int i = 0; i < numberOfPackets; i++)
            {
                short numSamples = dataReader.ReadInt16();
                dataReader.Serialize(inputSamples, 0, numSamples);
                int numDecoded = celt.Decode(inputSamples, numSamples, outputSamples);                
                foreach (var sample in outputSamples.Skip(celt.GetDecoderSampleDelay() * channels))
                {                    
                    tempWriter.Write(sample);
                }                
            }

            tempStream.Close();

            Directory.CreateDirectory(Path.GetDirectoryName(dst));
            using var ffmpeg = new System.Diagnostics.Process();            
            ffmpeg.StartInfo.FileName = "ffmpeg.exe";
            ffmpeg.StartInfo.Arguments = $@"-f f32le -ar {sampleRate} -ac {channels} -i {tempFile} {dst}";
            ffmpeg.StartInfo.UseShellExecute = false;
            ffmpeg.StartInfo.RedirectStandardOutput = true;
            ffmpeg.StartInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
            ffmpeg.StartInfo.CreateNoWindow = true;
            ffmpeg.Start();
            ffmpeg.WaitForExit();

            File.Delete(tempFile);
        }

        public static async void SoundToXenko(string src, string root, string assetPath)
        {
            if (FFmpegPath == string.Empty)
            {
                if (!FindFFmpeg())
                {
                    Console.Error.WriteLine("Error: Failed to find FFmpeg.");
                    throw new NotSupportedException("Failed to find FFmpeg.");
                }
            }
            var dst = Path.Combine(root, assetPath);
            Directory.CreateDirectory(Directory.GetParent(dst).FullName);

            using var srcFs = File.OpenRead(src);

            var tempFile = Path.GetTempFileName();

            var options = new FFOptions();
            options.BinaryFolder = FFmpegPath;            
            GlobalFFOptions.Configure(options);
            try
            {
                FFProbeHelper.VerifyFFProbeExists(options);
            }
            catch (Exception) //Eat "Process already exited"
            {
            }

            const int outputBitrate = 48000;

            var mediaInfo = FFProbe.Analyse(src, options);
            var audioStream = mediaInfo.AudioStreams.First();
            using var ffmpeg = new System.Diagnostics.Process();
            ffmpeg.StartInfo.FileName = "ffmpeg.exe";
            ffmpeg.StartInfo.Arguments = $@"-i {src} -f f32le -ar {outputBitrate} - ac {audioStream.Channels} -y {tempFile}";
            ffmpeg.StartInfo.UseShellExecute = false;
            ffmpeg.StartInfo.RedirectStandardOutput = true;
            ffmpeg.StartInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
            ffmpeg.StartInfo.CreateNoWindow = true;
            ffmpeg.Start();
            ffmpeg.WaitForExit();    

            using var celt = new Celt(48000, SamplesPerFrame, audioStream.Channels, false);

            using var tmpReader = new BinaryReader(new FileStream(tempFile, FileMode.Open));
            using var dataOutputStream = new FileStream(dst+"_Data", FileMode.Create);
            using var dataOutputWriter = new BinaryWriter(dataOutputStream);
            
            var length = tmpReader.BaseStream.Length / sizeof(float);
            var padding = audioStream.Channels * celt.GetDecoderSampleDelay();
            var frameSize = SamplesPerFrame * audioStream.Channels;
            var targetSize = SamplesPerFrame * audioStream.Channels * sizeof(short);
            var outputBuffer = new byte[targetSize];
            var buffer = new float[frameSize];

            var samples = 0;
            var numberOfPackets = 0;
            var maxPacketLength = 0;
            int position = 0;
            for (; position < length+padding; position++)
            {
                if(position % frameSize == 0)
                {
                    var len = celt.Encode(buffer, outputBuffer);
                    dataOutputWriter.Write((short)len);
                    dataOutputStream.Write(outputBuffer,0,len);

                    samples += frameSize / audioStream.Channels;
                    numberOfPackets++;
                    maxPacketLength = Math.Max(len, maxPacketLength);

                    Array.Clear(buffer, 0, frameSize);
                }
                buffer[position % frameSize] = (position < length) ? tmpReader.ReadSingle() : 0.0f;
            }

            if(position%frameSize > 0)
            {
                var len = celt.Encode(buffer, outputBuffer);
                dataOutputWriter.Write((short)len);
                dataOutputStream.Write(outputBuffer, 0, len);

                samples += frameSize / audioStream.Channels;
                numberOfPackets++;
                maxPacketLength = Math.Max(len, maxPacketLength);
            }

            dataOutputStream.Close();

            using var outputStream = new FileStream(dst, FileMode.Create);
            var outputWriter = new BinarySerializationWriter(outputStream);
        
            var header = new ChunkHeader { Type = typeof(Sound).AssemblyQualifiedName };
            header.Write(outputWriter);
            header.OffsetToReferences = (int)outputWriter.NativeStream.Position;

            var refsPath = src + ".refs";
            if (!File.Exists(refsPath))
                outputWriter.Write(0);
            else
            {
                var refBytes = File.ReadAllBytes(refsPath);
                await outputWriter.NativeStream.WriteAsync(refBytes, 0, refBytes.Length);
            }
            header.OffsetToObject = (int)outputWriter.NativeStream.Position;
            outputWriter.Write((byte)0);
            outputWriter.Write(assetPath+"_Data");
            outputWriter.Write(outputBitrate);
            outputWriter.Write((byte)audioStream.Channels);
            outputWriter.Write(false);
            outputWriter.Write(audioStream.Channels > 1);
            outputWriter.Write(numberOfPackets);
            outputWriter.Write((short)maxPacketLength);
            outputWriter.Write(samples);
            outputWriter.NativeStream.Position = 0;
            header.Write(outputWriter);
        }
    }
}
