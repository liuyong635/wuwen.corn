using OpenCvSharp;
using SeedCut.Framework.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SeedCut.OpenCV
{
    public class ImageDiff
    {
        public static async Task<Mat> GetImageFromVM(IVisionDevice vision, IHandlerContext ctx, string prod = "小料盒", string path = @"D:\Wuwen\SeedCut\Temp\小料盒.bmp")
        {
            DateTime now = DateTime.Now.ToLocalTime();
            var visionResult = await vision.ExecuteAsync(prod, ctx);

            CancellationToken token = new CancellationTokenSource(2000).Token;
            bool canRead = false;
            do
            {
                FileInfo fileInfo = new FileInfo(path);

                if (fileInfo.Exists && fileInfo.CreationTime > now)
                {
                    
                    FileStream fileStream = new FileStream(fileInfo.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);
                    if (fileStream.CanRead)
                    {
                        canRead = true;
                        fileStream.Dispose();
                        break;
                    }

                    fileStream.Dispose();
                }

            } while (!token.IsCancellationRequested);

            if (canRead)
            {
                return Cv2.ImRead(path, ImreadModes.Grayscale);
            }


            return null;

        }
        public static bool IsDiff(Mat imag1, Mat imag2)
        {
            bool isDiff = false;
            Mat diff = new Mat();
            Cv2.Absdiff(imag1, imag2, diff);
            Mat imagThr = new Mat();
            double threshold = Cv2.Threshold(diff, imagThr, 30, 50, ThresholdTypes.Binary);
            Point[][] cos;
            HierarchyIndex[] outputArray;
            Cv2.FindContours(imagThr, out cos, out outputArray, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

            Mat test = new Mat(imag2.Width, imag2.Height, MatType.CV_32FC3);
            int largeShape = -1;
            double area = -1;
            if (cos.Length > 0)
            {
                int cout = cos.Length;
                for (int i = 0; i < cout; i++)
                {
                    double _area = Cv2.ContourArea(cos[i]);
                    if (_area > area)
                    {
                        largeShape = i;
                        area = _area;
                    }

                }

            }

            if (largeShape > 0 && area > 10000)
            {
                isDiff = true;


            }
            Cv2.Resize(test, test, new OpenCvSharp.Size(640, test.Width / (float)test.Height * 640));
            Cv2.ImShow("test", test);

         
            test.Dispose();
            imagThr.Dispose();
            return isDiff;
        }
    }
}
