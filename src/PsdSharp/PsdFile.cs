﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace PsdSharp
{
    public enum ImageCompression
    {
        /// <summary>Raw data</summary>
        Raw = 0,

        /// <summary>RLE compressed</summary>
        Rle = 1,

        /// <summary>ZIP without prediction.</summary>
        /// <remarks>
        /// Not supported. Loading will result in an image 
        /// where all channels are set to zero.</remarks>
        Zip = 2,

        /// <summary>ZIP with prediction.</summary>
        /// <remarks>
        /// Not supported. Loading will result in an image 
        /// where all channels are set to zero.</remarks>
        ZipPrediction = 3
    }

    public class PsdFile
    {
        public enum ColorModes
        {
            Bitmap = 0,
            Grayscale = 1,
            Indexed = 2,
            RGB = 3,
            CMYK = 4,
            Multichannel = 7,
            Duotone = 8,
            Lab = 9
        }

        /// <summary>If ColorMode is ColorModes.Indexed, the following 768 bytes will contain 
        /// a 256 color palette. If the ColorMode is ColorModes.Duotone, the data following 
        /// presumably consists of screen parameters and other related information.</summary>
        public byte[] ColorModeData = new byte[0];

        /// <summary>Masking data for the PSD</summary>
        private byte[] GlobalLayerMaskData = new byte[0];

        /// <summary>The raw image data from the file, 
        /// seperated by the channels.</summary>
        private int columns;

        private int depth;

        private int rows;

        private short channels;

        public PsdFile()
        {
        }

        public bool AbsoluteAlpha { get; set; }

        public byte[][] ImageData { get; set; }

        /// <summary>The color mode of the file.</summary>
        public ColorModes ColorMode { get; set; }

        public ImageCompression Compression { get; set; }

        /// <summary>The width of the image in pixels.</summary>
        public int Columns
        {
            get { return columns; }
            set
            {
                if (value < 0 || value > 30000)
                    throw new ArgumentException("Supported range is 1 to 30000.");
                columns = value;
            }
        }

        /// <summary>The number of bits per channel. 
        /// Supported values are 1, 8, and 16.</summary>

        // TODO: Use enum instead
        public int Depth
        {
            get { return depth; }
            set
            {
                if (value == 1 || value == 8 || value == 16)
                    depth = value;
                else
                    throw new ArgumentException("Supported values are 1, 8, and 16.");
            }
        }

        /// <summary>The height of the image in pixels.</summary>
        public int Rows
        {
            get { return rows; }
            set
            {
                if (value < 0 || value > 30000)
                    throw new ArgumentException("Supported range is 1 to 30000.");
                rows = value;
            }
        }

        /// <summary>The Image resource blocks for the file.</summary>
        public List<ImageResource> ImageResources { get; private set; }

        public List<Layer> Layers { get; private set; }

        public ResolutionInfo Resolution
        {
            get { return (ResolutionInfo) ImageResources.Find(x => x.Id == (int) ResourceIDs.ResolutionInfo); }

            set
            {
                ImageResource oldValue = ImageResources.Find(x => x.Id == (int) ResourceIDs.ResolutionInfo);

                if (oldValue != null)
                    ImageResources.Remove(oldValue);

                ImageResources.Add(value);
            }
        }

        /// <summary>The number of channels in the image, including 
        /// any alpha channels. Supported range is 1 to 24.</summary>
        public short Channels
        {
            get { return channels; }
            set
            {
                if (value < 1 || value > 24)
                    throw new ArgumentException("Supported range is 1 to 24");
                channels = value;
            }
        }

        /// <summary>
        /// Always equal to 1.
        /// </summary>
        public short Version { get; private set; } = 1;

        public PsdFile(string filename)
        {
            Load(filename);
            //compositImage = ImageDecoder.DecodeImage(this);
        }

        public PsdFile(Stream stream)
        {
            Load(stream);
            //compositImage = ImageDecoder.DecodeImage(this);
        }

        public void Load(string filename)
        {
            using (MemoryStream stream = new MemoryStream(File.ReadAllBytes(filename)))
            {
                Load(stream);
            }
        }

        public void Load(Stream stream)
        {
            //binary reverse reader reads data types in big-endian format.
            BinaryReverseReader reader = new BinaryReverseReader(stream);

            //The headers area is used to check for a valid PSD file
            Debug.WriteLine("LoadHeader started at " + reader.BaseStream.Position);

            string signature = new string(reader.ReadChars(4));
            if (signature != "8BPS")
                throw new IOException("Bad or invalid file stream supplied");

            //get the version number, should be 1 always
            if ((Version = reader.ReadInt16()) != 1)
                throw new IOException("Invalid version number supplied");

            //get rid of the 6 bytes reserverd in PSD format
            reader.BaseStream.Position += 6;

            //get the rest of the information from the PSD file.
            //Everytime ReadInt16() is called, it reads 2 bytes.
            //Everytime ReadInt32() is called, it reads 4 bytes.
            channels = reader.ReadInt16();
            rows = reader.ReadInt32();
            columns = reader.ReadInt32();
            depth = reader.ReadInt16();
            ColorMode = (ColorModes) reader.ReadInt16();

            //by end of headers, the reader has read 26 bytes into the file.

            /// <summary>
            /// If ColorMode is ColorModes.Indexed, the following 768 bytes will contain
            /// a 256-color palette. If the ColorMode is ColorModes.Duotone, the data
            /// following presumably consists of screen parameters and other related information.
            /// Unfortunately, it is intentionally not documented by Adobe, and non-Photoshop
            /// readers are advised to treat duotone images as gray-scale images.
            /// </summary>
            Debug.WriteLine("LoadColorModeData started at " + reader.BaseStream.Position);

            uint paletteLength = reader.ReadUInt32(); //readUint32() advances the reader 4 bytes.
            if (paletteLength > 0)
            {
                ColorModeData = reader.ReadBytes((int) paletteLength);
            }

            //This part takes extensive use of classes that I didn't write therefore
            //I can't document much on what they do.

            Debug.WriteLine("LoadingImageResources started at " + reader.BaseStream.Position);

            ImageResources.Clear();

            uint imgResLength = reader.ReadUInt32();
            long startPosition = reader.BaseStream.Position;
            if (imgResLength > 0)
            {
                while (reader.BaseStream.Position - startPosition < imgResLength)
                {
                    ImageResource imgRes = new ImageResource(reader);

                    ResourceIDs resID = (ResourceIDs) imgRes.Id;
                    switch (resID)
                    {
                        case ResourceIDs.ResolutionInfo:
                            imgRes = new ResolutionInfo(imgRes);
                            break;
                        case ResourceIDs.Thumbnail1:
                        case ResourceIDs.Thumbnail2:
                            imgRes = new Thumbnail(imgRes);
                            break;
                        case ResourceIDs.AlphaChannelNames:
                            imgRes = new AlphaChannels(imgRes);
                            break;
                    }

                    ImageResources.Add(imgRes);
                }
            }
            // make sure we are not on a wrong offset, so set the stream position
            // manually
            reader.BaseStream.Position = startPosition + imgResLength;

            //We are gonna load up all the layers and masking of the PSD now.
            Debug.WriteLine("LoadLayerAndMaskInfo - Part1 started at " + reader.BaseStream.Position);
            uint layersAndMaskLength = reader.ReadUInt32();

            if (layersAndMaskLength > 0)
            {
                //new start position
                startPosition = reader.BaseStream.Position;

                //Lets start by loading up all the layers
                LoadLayers(reader);
                //we are done the layers, load up the masks
                LoadGlobalLayerMask(reader);

                // make sure we are not on a wrong offset, so set the stream position
                // manually
                reader.BaseStream.Position = startPosition + layersAndMaskLength;

                //we have loaded up all the information from the PSD file
                //into variables we can use later on.

                //lets finish loading the raw data that defines the image
                //in the picture.

                Debug.WriteLine("LoadImage started at " + reader.BaseStream.Position);

                Compression = (ImageCompression) reader.ReadInt16();

                ImageData = new byte[channels][];

                //---------------------------------------------------------------

                if (Compression == ImageCompression.Rle)
                {
                    // The RLE-compressed data is proceeded by a 2-byte data count for each row in the data,
                    // which we're going to just skip.
                    reader.BaseStream.Position += rows * channels * 2;
                }

                //---------------------------------------------------------------

                int bytesPerRow = 0;

                switch (depth)
                {
                    case 1:
                        bytesPerRow = columns; //NOT Sure
                        break;
                    case 8:
                        bytesPerRow = columns;
                        break;
                    case 16:
                        bytesPerRow = columns * 2;
                        break;
                }

                //---------------------------------------------------------------

                for (int ch = 0; ch < channels; ch++)
                {
                    ImageData[ch] = new byte[rows * bytesPerRow];

                    switch (Compression)
                    {
                        case ImageCompression.Raw:
                            reader.Read(ImageData[ch], 0, ImageData[ch].Length);
                            break;
                        case ImageCompression.Rle:
                        {
                            for (int i = 0; i < rows; i++)
                            {
                                int rowIndex = i * columns;
                                RleUtility.DecodedRow(reader.BaseStream, ImageData[ch], rowIndex, bytesPerRow);
                            }
                        }
                            break;
                    }
                }
            }
        }

        /// <summary>Load up the masking information of the supplied PSD.</summary>        
        private void LoadGlobalLayerMask(BinaryReverseReader reader)
        {
            Debug.WriteLine("LoadGlobalLayerMask started at " + reader.BaseStream.Position);

            uint maskLength = reader.ReadUInt32();

            if (maskLength <= 0)
                return;

            GlobalLayerMaskData = reader.ReadBytes((int) maskLength);
        }

        /// <summary>Loads up the Layers of the supplied PSD file.</summary>      
        private void LoadLayers(BinaryReverseReader reader)
        {
            Debug.WriteLine("LoadLayers started at " + reader.BaseStream.Position);

            uint layersInfoSectionLength = reader.ReadUInt32();

            if (layersInfoSectionLength <= 0)
                return;

            long startPosition = reader.BaseStream.Position;

            short numberOfLayers = reader.ReadInt16();

            // If <0, then number of layers is absolute value,
            // and the first alpha channel contains the transparency data for
            // the merged result.
            if (numberOfLayers < 0)
            {
                AbsoluteAlpha = true;
                numberOfLayers = Math.Abs(numberOfLayers);
            }

            Layers.Clear();

            if (numberOfLayers == 0)
                return;

            for (int i = 0; i < numberOfLayers; i++)
            {
                Layers.Add(new Layer(reader, this));
            }

            foreach (Layer layer in Layers)
            {
                foreach (Layer.Channel channel in layer.Channels)
                {
                    if (channel.Id != -2)
                        channel.LoadPixelData(reader);
                }
                layer.MaskData.LoadPixelData(reader);
            }

            if (reader.BaseStream.Position % 2 == 1)
                reader.ReadByte();

            // make sure we are not on a wrong offset, so set the stream position
            // manually
            reader.BaseStream.Position = startPosition + layersInfoSectionLength;
        }
    }
}