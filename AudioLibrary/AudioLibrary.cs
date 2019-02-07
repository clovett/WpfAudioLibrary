using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using MediaFoundation.Misc;
using MediaFoundation;
using System.Diagnostics;
using MediaFoundation.ReadWrite;
using System.IO;

namespace AudioLibrary
{
    public class AudioDevice
    {
        internal AudioDevice(IMFActivate dev)
        {
            Device = dev;

            int cItems;
            HResult hr = dev.GetCount(out cItems);
            NativeHelpers.CheckHr(hr, "IMFActivate.GetCount");
            for (int j = 0; j < cItems; j++)
            {
                Guid key;
                using (PropVariant value = new PropVariant())
                {
                    hr = dev.GetItemByIndex(j, out key, value);
                    Debug.WriteLine("{0}={1}", key.ToString(), value.ToString());
                    if (key == MFAttributesClsid.MF_DEVSOURCE_ATTRIBUTE_FRIENDLY_NAME)
                    {
                        Name = value.ToString();
                    }
                    else if (key == MFAttributesClsid.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_AUDCAP_SYMBOLIC_LINK)
                    {
                        SymbolicLink = value.ToString();
                    }
                    else if (key == MFAttributesClsid.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_AUDCAP_ENDPOINT_ID)
                    {
                        EndPointId = value.ToString();
                    }

                }
            }
        }

        public string Name { get; set; }

        public string SymbolicLink { get; set; }

        public string EndPointId { get; set; }

        internal IMFActivate Device { get; set; }

        public override string ToString()
        {
            return this.Name;
        }

        public AudioRecording StartRecording(string wavFileName, EventHandler<AudioSample> pcmHandler)
        {
            if (this.recording == null)
            {
                Guid iid = typeof(IMFMediaSource).GUID;
                object ppv;
                HResult hr = this.Device.ActivateObject(iid, out ppv);
                NativeHelpers.CheckHr(hr, "Activating audio device");
                this.source = (IMFMediaSource)ppv;
                this.recording = new AudioRecording(this.source);
            }
            this.recording.Start(wavFileName, pcmHandler);
            return this.recording;
        }

        private IMFMediaSource source;
        private AudioRecording recording;
    }

    public class AudioSample
    {
        public string Error { get; set; }
        public byte[] Data { get; set; }
        public bool Closed { get; set; }
    }

    public class AudioRecording : IDisposable, IMFSourceReaderCallback
    {
        IMFSourceReader reader;
        string wavFileName;
        Stream fileStream;
        bool disposed;
        EventHandler<AudioSample> pcmHandler;
        BinaryWriter writer;
        uint dataSize;
        IMFMediaType audioType;
        bool recording;
        object recordingObject = new object(); // for synchronizing callback thread
        int channels;
        int sampleRate;
        int bitsPerSample;
        int wavHeaderSize;
        bool loading; // only reading a file, no capture or conversion going on.

        internal AudioRecording(IMFMediaSource source)
        {
            OpenMediaSource(source);
            this.audioType = ConfigureAudioStream();
            Marshal.ReleaseComObject(source);
        }

        internal void Start(string wavFileName, EventHandler<AudioSample> pcmHandler)
        {
            this.reader.Flush((int)MF_SOURCE_READER.FirstAudioStream);
            this.pcmHandler = pcmHandler;
            this.dataSize = 0;
            CloseFile();
            CreateWavFile(wavFileName);
            WriteWaveHeader(audioType, 0);
            this.recording = true;
            this.loading = false;
            ReadNextFrame();
        }


        public AudioRecording(string audioFileToConvert, string newWavFile, EventHandler<AudioSample> pcmHandler)
        {
            this.pcmHandler = pcmHandler;
            OpenMediaSource(audioFileToConvert);
            this.audioType = ConfigureAudioStream();

            if (newWavFile != audioFileToConvert)
            {
                this.loading = false;
                CreateWavFile(newWavFile);
                WriteWaveHeader(audioType, 0);
            }
            else
            {
                this.loading = true;
                GetAudioFormat(this.audioType);
                this.wavFileName = audioFileToConvert;
            }
            this.recording = true;
            ReadNextFrame();
        }

        public bool Loading {  get { return this.loading; } }

        public string FileName {  get { return wavFileName;  } }

        public int Channels {  get => this.channels;  }

        public int SampleRate {  get => this.sampleRate;  }

        public int BitsPerSample { get => this.bitsPerSample; }

        public float[] ConvertToFloat(byte[] data)
        {
            float range = (float)Math.Pow(2, this.bitsPerSample - 1); // signed value range
            int bytesPerSample = this.bitsPerSample / 8;
            int samples = data.Length / bytesPerSample;
            float[] buffer = new float[samples];
            BinaryReader reader = new BinaryReader(new MemoryStream(data));
            for (int i = 0; i < samples; i++)
            {
                switch (bytesPerSample)
                {
                    case 1:
                        buffer[i] = (float)reader.ReadSByte() / range;
                        break;
                    case 2:
                        buffer[i] = (float)reader.ReadInt16() / range;
                        break;
                    case 4:
                        buffer[i] = (float)reader.ReadInt32() / range;
                        break;
                    default:
                        throw new Exception("Unexpected bytes per sample :" + bytesPerSample.ToString());
                }

            }
            return buffer;
        }

        void OpenMediaSource(IMFMediaSource source)
        {
            IMFAttributes pAttributes = null;

            HResult hr = MFExtern.MFCreateAttributes(out pAttributes, 2);
            NativeHelpers.CheckHr(hr, "MFCreateAttributes");

            hr = pAttributes.SetUnknown(MFAttributesClsid.MF_SOURCE_READER_ASYNC_CALLBACK, this);
            NativeHelpers.CheckHr(hr, "SetUnknown");

            IMFSourceReader reader;
            hr = MFExtern.MFCreateSourceReaderFromMediaSource(source, pAttributes, out reader);
            NativeHelpers.CheckHr(hr, "MFCreateSourceReaderFromMediaSource");
            this.reader = reader;
        }

        void OpenMediaSource(string filename)
        {
            IMFAttributes pAttributes = null;

            HResult hr = MFExtern.MFCreateAttributes(out pAttributes, 2);
            NativeHelpers.CheckHr(hr, "MFCreateAttributes");

            hr = pAttributes.SetUnknown(MFAttributesClsid.MF_SOURCE_READER_ASYNC_CALLBACK, this);
            NativeHelpers.CheckHr(hr, "SetUnknown");

            IMFSourceReader reader;
            hr = MFExtern.MFCreateSourceReaderFromURL(filename, pAttributes, out reader);
            NativeHelpers.CheckHr(hr, "MFCreateSourceReaderFromURL");
            this.reader = reader;
        }

        ~AudioRecording()
        {
            if (!disposed)
            {
                Dispose();
            }
        }

        public void Dispose()
        {
            disposed = true;
            CloseFile();
            if (this.reader != null)
            {
                Marshal.ReleaseComObject(reader);
                this.reader = null;
            }
            GC.SuppressFinalize(true);
        }

        private void CreateWavFile(string wavFileName)
        {
            this.wavFileName = wavFileName;
            this.fileStream = new FileStream(wavFileName, FileMode.OpenOrCreate, FileAccess.Write);
            this.writer = new BinaryWriter(this.fileStream, Encoding.UTF8, true);
        }

        private WaveFormatEx GetAudioFormat(IMFMediaType mediaType)
        {
            WaveFormatEx pWav = null;
            int wavHeaderSize;

            // Convert the PCM audio format into a WAVEFORMATEX structure.
            HResult hr = MFExtern.MFCreateWaveFormatExFromMFMediaType(mediaType, out pWav, out wavHeaderSize, MFWaveFormatExConvertFlags.Normal);
            NativeHelpers.CheckHr(hr, "MFCreateWaveFormatExFromMFMediaType");

            this.wavHeaderSize = wavHeaderSize;
            this.channels = pWav.nChannels;
            this.sampleRate = pWav.nSamplesPerSec;
            this.bitsPerSample = pWav.wBitsPerSample;
            return pWav;
        }

        private void WriteWaveHeader(IMFMediaType mediaType, uint dataSize)
        {
            WaveFormatEx pWav = GetAudioFormat(mediaType);
            
            uint headerSize = (uint)(5 * 4); // 5 uint's
            uint dataHeaderSize = (2 * 4); // 2 uint's
            uint fileSize = headerSize + (uint)this.wavHeaderSize + dataHeaderSize + dataSize - 8;

            // Write the 'RIFF' header and the start of the 'fmt ' chunk.
            uint[] header = { 
                // RIFF header
                (uint)(new FourCC("RIFF").ToInt32()),
                fileSize,
                (uint)(new FourCC("WAVE").ToInt32()),  
                // Start of 'fmt ' chunk
                (uint)(new FourCC("fmt ").ToInt32()),
                (uint)wavHeaderSize
            };

            uint[] dataHeader = {
                 (uint)(new FourCC("data").ToInt32()),
                 dataSize,
                (uint)(new FourCC("RIFF").ToInt32()),
            };

            WriteToFile(header);

            // Write the WAVEFORMATEX structure.
            IntPtr data = pWav.GetPtr();
            WriteToFile(data, wavHeaderSize);
            Marshal.FreeCoTaskMem(data);

            // Write the start of the 'data' chunk
            WriteToFile(dataHeader);
        }

        public void ReadNextFrame()
        {
            HResult hr = this.reader.ReadSample((int)MF_SOURCE_READER.FirstAudioStream, MF_SOURCE_READER_CONTROL_FLAG.None, IntPtr.Zero,
                IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            NativeHelpers.CheckHr(hr, "ReadSample");
        }

        void HandleError(string message)
        {
            if (this.pcmHandler != null)
            {
                this.pcmHandler(this, new AudioSample() { Error = message });
            }
            this.recording = false;
        }

        private void WriteToFile(IntPtr data, int size)
        {
            byte[] buffer = new byte[size];
            Marshal.Copy(data, buffer, 0, size);
            this.writer.Write(buffer, 0, size);
        }

        private void WriteToFile(uint[] header)
        {
            foreach (uint i in header)
            {
                this.writer.Write(i);
            }
        }


        private IMFMediaType ConfigureAudioStream()
        {
            IMFMediaType pPartialType = null;
            IMFMediaType pUncompressedAudioType = null;

            // Create a partial media type that specifies uncompressed PCM audio.

            HResult hr = MFExtern.MFCreateMediaType(out pPartialType);
            NativeHelpers.CheckHr(hr, "MFCreateMediaType");

            hr = pPartialType.SetGUID(MFAttributesClsid.MF_MT_MAJOR_TYPE, MFMediaType.Audio);
            NativeHelpers.CheckHr(hr, "SetGUID(MF_MT_MAJOR_TYPE,Audio)");

            hr = pPartialType.SetGUID(MFAttributesClsid.MF_MT_SUBTYPE, MFMediaType.PCM);
            NativeHelpers.CheckHr(hr, "SetGUID(MF_MT_SUBTYPE,PCM)");

            // Set this type on the source reader. The source reader will load the necessary decoder.
            hr = this.reader.SetCurrentMediaType((int)MF_SOURCE_READER.FirstAudioStream, null, pPartialType);

            NativeHelpers.CheckHr(hr, "SetCurrentMediaType(MF_SOURCE_READER.FirstAudioStream)");

            // Get the complete uncompressed format.
            hr = this.reader.GetCurrentMediaType((int)MF_SOURCE_READER.FirstAudioStream, out pUncompressedAudioType);
            NativeHelpers.CheckHr(hr, "GetCurrentMediaType(MF_SOURCE_READER.FirstAudioStream)");

            // Ensure the stream is selected.
            hr = this.reader.SetStreamSelection((int)MF_SOURCE_READER.FirstAudioStream, true);
            NativeHelpers.CheckHr(hr, "SetStreamSelection");

            // Return the PCM format to the caller.
            return pUncompressedAudioType;
        }

        public void StopRecording()
        {
            lock (this.recordingObject)
            {
                CloseFile();
            }
        }

        void CloseFile()
        {
            if (this.writer != null)
            {
                this.writer.Flush();
            }

            if (this.fileStream != null)
            {
                this.fileStream.Flush();
                this.fileStream.Seek(0, SeekOrigin.Begin);
                this.writer = new BinaryWriter(this.fileStream, Encoding.UTF8, true);
                // fix up the wav header so file size is correct.
                WriteWaveHeader(this.audioType, this.dataSize);

                using (this.fileStream)
                {
                    this.fileStream.Flush();
                    this.fileStream.Close();
                    this.fileStream = null;
                }
            }
            this.writer = null;
        }

        public HResult OnReadSample(HResult hr, int dwStreamIndex, MF_SOURCE_READER_FLAG dwStreamFlags, long llTimestamp, IMFSample pSample)
        {
            // make sure user still wants us to be recording...
            lock (this.recordingObject)
            {
                if (this.recording)
                {
                    if (hr != 0)
                    {
                        HandleError("Error reading audio: " + hr.ToString());
                    }
                    else 
                    {
                        PublishSample(pSample, (dwStreamFlags & MF_SOURCE_READER_FLAG.EndOfStream) != 0);
                    }
                }
            }
            return HResult.S_OK;
        }

        private void PublishSample(IMFSample sample, bool closed)
        {
            if (sample == null)
            {
                // then it is just a tick event
                if (this.pcmHandler != null)
                {
                    this.pcmHandler(this, new AudioSample() { Closed = closed });
                }
            }
            else 
            {
                IMFMediaBuffer buffer;
                HResult hr = sample.ConvertToContiguousBuffer(out buffer);
                if (hr != 0)
                {
                    HandleError("ConvertToContiguousBuffer failed: " + hr.ToString());
                }
                else
                {
                    IntPtr ptr;
                    int maxLength;
                    int currentLength;
                    buffer.Lock(out ptr, out maxLength, out currentLength);
                    byte[] data = new byte[currentLength];
                    Marshal.Copy(ptr, data, 0, currentLength);
                    buffer.Unlock();

                    if (this.pcmHandler != null)
                    {
                        this.pcmHandler(this, new AudioSample() { Data = data, Closed = closed });
                    }

                    if (this.writer != null)
                    {
                        this.writer.Write(data, 0, currentLength);
                    }
                    this.dataSize += (uint)currentLength;
                }
            }
        }

        public HResult OnFlush(int dwStreamIndex)
        {
            return HResult.S_OK;
        }

        public HResult OnEvent(int dwStreamIndex, IMFMediaEvent pEvent)
        {
            MediaEventType pmet;
            pEvent.GetType(out pmet);
            Debug.WriteLine(pmet.ToString());
            return HResult.S_OK;
        }
    }


    public class AudioConfiguration
    {
        public AudioConfiguration()
        {
            HResult hr = MediaFoundation.MFExtern.MFStartup(0x70, MFStartup.Lite);
            NativeHelpers.CheckHr(hr, "Starting up Windows Media Foundation Services");
        }

        public List<AudioDevice> ListDevices()
        {
            List<AudioDevice> result = new List<AudioDevice>();
            IMFAttributes mfa;
            HResult hr = MediaFoundation.MFExtern.MFCreateAttributes(out mfa, 1);
            NativeHelpers.CheckHr(hr, "Creating Media Foundation Attributes");

            // Request audio capture devices.
            hr = mfa.SetGUID(
                MFAttributesClsid.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE,
                CLSID.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_AUDCAP_GUID
            );
            NativeHelpers.CheckHr(hr, "Setting the Source Type on IMFAttributes");

            // Enumerate the devices,
            IMFActivate[] devices;
            int count = 0;
            hr = MediaFoundation.MFExtern.MFEnumDeviceSources(mfa, out devices, out count);
            NativeHelpers.CheckHr(hr, "MFEnumDeviceSources");

            for (int i = 0; i < count; i++)
            {
                IMFActivate pdev = devices[i];
                result.Add(new AudioDevice(pdev));
            }
            if (count > 0)
            {
                //hr = ppDevices[0]->ActivateObject(IID_PPV_ARGS(ppSource));
            }
            else
            {
                //hr = MF_E_NOT_FOUND;
            }

            return result;
        }

    }
}
