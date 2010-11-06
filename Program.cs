#region PDFsharp - A .NET library for processing PDF
//
// Authors:
//   PDFsharp Team (mailto:PDFsharpSupport@pdfsharp.de)
//
// Copyright (c) 2005-2009 empira Software GmbH, Cologne (Germany)
//
// http://www.pdfsharp.com
// http://sourceforge.net/projects/pdfsharp
//
// Permission is hereby granted, free of charge, to any person obtaining a
// copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included
// in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
// THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.
#endregion

using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;
using PdfSharp.Pdf.Filters;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace ExportImages
{
    class Program
    {
        static void Main()
        {
            const string filename = @"Sample.pdf";

            PdfDocument document = PdfReader.Open(filename);

            int imageCount = 0;

            var objects = document.Internals.GetAllObjects();

            foreach (var item in objects)
            {
                PdfDictionary xObject = item as PdfDictionary;
                // Is external object an image?
                if (xObject != null && xObject.Elements.GetString(PdfImage.Keys.Subtype) == "/Image")
                {
                    ExportImage(xObject, ref imageCount);
                }

            }

            Console.WriteLine("{0} Images", imageCount);
        }

        static void ExportImage(PdfDictionary image, ref int count)
        {
            string filter = image.Elements.GetName(PdfImage.Keys.Filter);
            switch (filter)
            {
                case "/DCTDecode":
                    ExportDCTEncodedImage(image, ref count);
                    break;

                case "/FlateDecode":
                    ExportFlateEncodedImage(image, ref count);
                    break;

                case "/CCITTFaxDecode":
                default:
                    break;
                    throw new NotSupportedException();
            }
        }

        static void ExportDCTEncodedImage(PdfDictionary image, ref int count)
        {
            byte[] stream = image.Stream.Value;
            using (FileStream fs = new FileStream(String.Format("Image{0}.jpg", count++), FileMode.Create, FileAccess.Write))
            {
                fs.Write(stream, 0, stream.Length);
            }
        }

        static void ExportFlateEncodedImage(PdfDictionary image, ref int count)
        {
            var colorspace_var = image.Elements[PdfImage.Keys.ColorSpace];

            int bits = image.Elements.GetInteger(PdfImage.Keys.BitsPerComponent);
            PixelFormat pixelformat = GetIndexedPixelFormat(bits);

            int width = image.Elements.GetInteger(PdfImage.Keys.Width);
            int height = image.Elements.GetInteger(PdfImage.Keys.Height);


            var colorspace_pdfarray = colorspace_var as PdfArray;

            string colorspace = null;
            {
                if (colorspace_pdfarray != null)
                {
                    colorspace = colorspace_pdfarray.Elements[0].ToString();

                }
                else if (colorspace_var.GetType() == typeof(PdfName))
                {
                    colorspace = (colorspace_var as PdfName).Value;
                }
                else
                {
                    throw new NotSupportedException();
                }
                switch (colorspace)
                {
                    case "/DeviceGray":
                    case "/Indexed":
                        break;
                    default:
                        bits = 24;
                        pixelformat = PixelFormat.Format24bppRgb;
                        break;
                }
            }

            using (var bmp = new Bitmap(width, height, pixelformat))
            {

                if (colorspace == "/Indexed")
                {
                    var palette = bmp.Palette;
                    var elements = colorspace_pdfarray.Elements;
                    for (int i = 1; i < elements.Count; ++i)
                    {
                        var elem = elements[i];
                        var type = elem.GetType();

                        int length = (1 << bits) - 1;
                        if (type == typeof(PdfInteger))
                        {
                            if ((elem as PdfInteger).Value != length)
                                throw new NotSupportedException();
                        }
                        else if (type == typeof(PdfString))
                        {
                            var data_enc = (elem as PdfString).Value.ToCharArray();

                            for (int p = 0; p <= length; ++p)
                            {
                                var colorp = palette.Entries[p];
                                palette.Entries[p] = Color.FromArgb(data_enc[3 * p], data_enc[3 * p + 1], data_enc[3 * p + 2]);
                            }

                        }
                    }
                    bmp.Palette = palette;
                }


                var bmd = bmp.LockBits(new Rectangle(Point.Empty, bmp.Size), ImageLockMode.WriteOnly, pixelformat);
                var data = image.Stream.UnfilteredValue;



                switch (colorspace)
                {
                    case "/DeviceGray":
                    case "/Indexed":
                        {
                            int bmd_h = bmd.Scan0.ToInt32();
                            int byte_h = 0;
                            int Stride_data = (width * bits + 7) / 8;

                            for (int h = 0; h < height; ++h)
                            {
                                Marshal.Copy(data, Stride_data * h, (IntPtr)bmd_h, Stride_data);
                                bmd_h += bmd.Stride;
                                byte_h += Stride_data;
                            }

                        }
                        break;
                    default:
                        {
                            int Stride_data = (width * bits + 7) / 8;
                            for (int h = 0; h < height; ++h)
                            {
                                for (int w = 0; w < width; ++w)
                                {
                                    Marshal.WriteByte(bmd.Scan0, h * bmd.Stride + 3 * w+2, data[h * Stride_data + 3 * w]);
                                    Marshal.WriteByte(bmd.Scan0, h * bmd.Stride + 3 * w + 1, data[h * Stride_data + 3 * w + 1]);
                                    Marshal.WriteByte(bmd.Scan0, h * bmd.Stride + 3 * w , data[h * Stride_data + 3 * w + 2]);
                                }
                            }
                        }
                        break;
                }


                bmp.UnlockBits(bmd);

                bmp.Save(String.Format("Image{0}.png", count++), ImageFormat.Png);
            }
        }


        static PixelFormat GetIndexedPixelFormat(int bits)
        {
            switch (bits)
            {
                case 1:
                    return PixelFormat.Format1bppIndexed;
                case 4:
                    return PixelFormat.Format4bppIndexed;
                case 8:
                    return PixelFormat.Format8bppIndexed;
                default:
                    throw new NotSupportedException();
            }
        }

    }
}