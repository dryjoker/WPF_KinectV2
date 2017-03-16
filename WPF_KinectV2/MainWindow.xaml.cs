using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Microsoft.Kinect;
using Microsoft.Kinect.Face; //https://www.nuget.org/packages/Microsoft.Kinect.Face.x64/  // use this to set Kinect Face Tracking (x64)
using Microsoft.ProjectOxford.Face;
using Microsoft.ProjectOxford.Face.Contract;
using System.IO;
using Emgu.CV;
using Emgu.CV.Structure;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using System.Drawing;
using System.Globalization;

namespace KinectV2_PhotoBooth
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            drawingGroup = new DrawingGroup();
            imageSource = new DrawingImage(drawingGroup);
            this.DataContext = this;
            InitializeComponent();
        }
        KinectSensor _sensor = null;
        MultiSourceFrameReader _reader;

        //BgRemoveHelper _bgRemovalHelper;
        BackgroundRemovalTool _bgRemovalHelper;

        BodyFrameReader bodyFrameReader;
        Body[] bodies = null;
        int bodyCount;

        FaceFrameSource[] faceFrameSources = null;
        FaceFrameReader[] faceFrameReaders = null;
        FaceFrameResult[] faceFrameResults = null;
        List<System.Windows.Media.Brush> faceBrush;

        // WPF
        DrawingGroup drawingGroup;
        DrawingImage imageSource;
        int displayWidth;
        int displayHeight;
        Rect displayRect;

        public ImageSource ImageSource
        {
            get
            {
                return this.imageSource;
            }
        }

        void InitializeFace()
        {
            FaceFrameFeatures faceFrameFeatures =
                    FaceFrameFeatures.BoundingBoxInColorSpace
                    | FaceFrameFeatures.PointsInColorSpace
                    | FaceFrameFeatures.RotationOrientation
                    | FaceFrameFeatures.FaceEngagement
                    | FaceFrameFeatures.Glasses
                    | FaceFrameFeatures.Happy
                    | FaceFrameFeatures.LeftEyeClosed
                    | FaceFrameFeatures.RightEyeClosed
                    | FaceFrameFeatures.LookingAway
                    | FaceFrameFeatures.MouthMoved
                    | FaceFrameFeatures.MouthOpen;
            faceFrameSources = new FaceFrameSource[bodyCount];
            faceFrameReaders = new FaceFrameReader[bodyCount];
            for (int i = 0; i < bodyCount; i++)
            {
                faceFrameSources[i] = new FaceFrameSource(_sensor, 0, faceFrameFeatures);
                faceFrameReaders[i] = faceFrameSources[i].OpenReader();
                faceFrameReaders[i].FrameArrived += faceFrameReader_FrameArrived;
            }
            faceFrameResults = new FaceFrameResult[bodyCount];

            //畫人臉偵測框的顏色種類存放用的 List
            faceBrush = new List<System.Windows.Media.Brush>()
                {
                    System.Windows.Media.Brushes.Brown,
                    System.Windows.Media.Brushes.Orange,
                    System.Windows.Media.Brushes.Green,
                    System.Windows.Media.Brushes.Red,
                    System.Windows.Media.Brushes.LightBlue,
                    System.Windows.Media.Brushes.Yellow
                };
        }

        private void faceFrameReader_FrameArrived(object sender, FaceFrameArrivedEventArgs e)
        {
            UpdateFaceFrame(e);
        }

        void UpdateFaceFrame(FaceFrameArrivedEventArgs e)
        {
            using (FaceFrame faceFrame = e.FrameReference.AcquireFrame())
            {
                if (faceFrame == null)
                {
                    return;
                }
                bool tracked;
                tracked = faceFrame.IsTrackingIdValid;
                if (!tracked)
                {
                    return;
                }

                FaceFrameResult faceResult = faceFrame.FaceFrameResult;
                int index = GetFaceSourceIndex(faceFrame.FaceFrameSource);
                faceFrameResults[index] = faceResult;
            }
        }

        int GetFaceSourceIndex(FaceFrameSource source)
        {
            int index = -1;
            for (int i = 0; i < bodyCount; i++)
            {
                if (faceFrameSources[i] == source)
                {
                    index = i;
                    break;
                }
            }
            return index;
        }

        void UpdateBodyFrame(BodyFrameArrivedEventArgs e)
        {
            using (var bodyFrame = e.FrameReference.AcquireFrame())
            {
                if (bodyFrame == null)
                {
                    return;
                }
                bodyFrame.GetAndRefreshBodyData(bodies);
                for (int i = 0; i < bodyCount; i++)
                {
                    Body body = bodies[i];
                    if (!body.IsTracked)
                    {
                        continue;
                    }
                    ulong trackingId = body.TrackingId;
                    faceFrameReaders[i].FaceFrameSource.TrackingId = trackingId;
                }
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            //haarCascade = new HaarCascade(@"haarcascade_frontalface_alt_tree.xml");
            _sensor = KinectSensor.GetDefault();

            if (_sensor != null) //當取得  sensor 之後
            {
                _sensor.Open(); //開啟

                //Initialize the background removal tool.
                //_bgRemovalHelper = new BgRemoveHelper(_sensor.CoordinateMapper);
                _bgRemovalHelper = new BackgroundRemovalTool(_sensor.CoordinateMapper);


                _reader = _sensor.OpenMultiSourceFrameReader(FrameSourceTypes.Color | FrameSourceTypes.Depth | FrameSourceTypes.BodyIndex | FrameSourceTypes.Body);
                _reader.MultiSourceFrameArrived += Reader_MultiSourceFrameArrived;

                FrameDescription frameDescription = _sensor.ColorFrameSource.FrameDescription;
                displayWidth = frameDescription.Width;
                displayHeight = frameDescription.Height;
                displayRect = new Rect(0, 0, displayWidth, displayHeight);

                bodyFrameReader = _sensor.BodyFrameSource.OpenReader();
                bodyFrameReader.FrameArrived += bodyFrameReader_FrameArrived;
                bodyCount = _sensor.BodyFrameSource.BodyCount;
                bodies = new Body[bodyCount];

                InitializeFace();
            }
        }

        private void bodyFrameReader_FrameArrived(object sender, BodyFrameArrivedEventArgs e)
        {
            UpdateBodyFrame(e);
            DrawFaceFrames();
        }

        private void DrawFaceFrames()
        {
            using (DrawingContext dc = drawingGroup.Open())
            {
                dc.DrawRectangle(System.Windows.Media.Brushes.Transparent, null, displayRect);

                //dc.DrawImage(b1.Source , displayRect);


                for (int i = 0; i < bodyCount; i++)
                {
                    if (faceFrameReaders[i].FaceFrameSource.IsTrackingIdValid)
                    {
                        if (faceFrameResults[i] != null)
                        {
                            DrawFaceFrameResult(i, faceFrameResults[i], dc);
                        }
                    }
                }
                drawingGroup.ClipGeometry = new RectangleGeometry(displayRect);
            }
        }

        bool object1 = false, object2 = false, object3 = false, object4 = false, object5 = false, object6 = false;

        void DrawFaceFrameResult(int faceIndex, FaceFrameResult faceResult, DrawingContext drawingContext)
        {
            //Brush/Pen
            System.Windows.Media.Brush drawingBrush = faceBrush[0];
            if (faceIndex < bodyCount)
            {
                drawingBrush = faceBrush[faceIndex];
            }
            System.Windows.Media.Pen drawingPen = new System.Windows.Media.Pen(drawingBrush, 5);

            //Face Points
            /*
            var facePoints = faceResult.FacePointsInColorSpace;
            foreach ( PointF pointF in facePoints.Values ) {
                drawingContext.DrawEllipse( null, drawingPen, new Point( pointF.X, pointF.Y ), 10, 10 );
            }
            */

            //Bounding Box
            RectI box = faceResult.FaceBoundingBoxInColorSpace;
            //double width = (box.Right - box.Left);
            //double height = (box.Bottom - box.Top);

            int width = box.Right - box.Left;
            int height = box.Bottom - box.Top;

            //Rect rect = new Rect(box.Left-80, box.Top-80, width*2, height*2);
            Rect rectW = new Rect(box.Left*0.33, box.Top* 0.44, width, height);
            drawingContext.DrawRectangle(null, drawingPen, rectW);
            
            //眼鏡物件 (*.png)
            if(object1 == true) // 180*112
            {
                Rect rect1 = new Rect(box.Left - 80, box.Top - 120, width * 2, height * 2);
                drawingContext.DrawImage(b1.Source, rect1);
            }
            if (object2 == true) //180*82
            {
                Rect rect2 = new Rect(box.Left - 80, box.Top - 120, width * 2.5, height * 2);
                drawingContext.DrawImage(b2.Source, rect2);
            }
            if (object3 == true) //180*94
            {
                Rect rect3 = new Rect(box.Left - 80, box.Top - 120, width * 2.5, height * 2);
                drawingContext.DrawImage(b3.Source, rect3);
            }

            //帽子物件 (*.png)
            if (object4 == true) //200*118
            {
                Rect rect4 = new Rect(box.Left - 80, box.Top - 300, width * 2, height * 2);
                drawingContext.DrawImage(b4.Source, rectW);
            }
            if (object5 == true) //200*132
            {
                Rect rect5 = new Rect(box.Left - 80, box.Top - 300, width * 2, height * 2);
                drawingContext.DrawImage(b5.Source, rect5);
            }
            if (object6 == true) //200*167
            {
                Rect rect6 = new Rect(box.Left - 80, box.Top - 300, width * 2, height * 2);
                drawingContext.DrawImage(b6.Source, rect6);
            }

            //String drawingText = String.Empty;


            //Vector4 quaternion = faceResult.FaceRotationQuaternion;
            //int offset = 30;
            //int pitch, yaw, roll;
            //quaternion2degree(quaternion, out pitch, out yaw, out roll);
            //drawingText = "Pitch, Yaw, Roll : " + pitch.ToString() + ", " + yaw.ToString() + ", " + roll.ToString();
            //FormattedText formattedText = new FormattedText(drawingText, CultureInfo.GetCultureInfo("ja-JP"), FlowDirection.LeftToRight, new Typeface("Georgia"), 25, drawingBrush);
            //drawingContext.DrawText(formattedText, new System.Windows.Point(box.Left, box.Bottom + offset));


            //Properties
            //if ( faceResult.FaceProperties!=null ) {
            //    foreach ( var item in faceResult.FaceProperties ) {
            //        drawingText = item.Key.ToString();
            //        switch ( item.Value ) {
            //        case DetectionResult.Yes:
            //            drawingText += " : Yes";
            //            break;
            //        case DetectionResult.Maybe:
            //            drawingText += " : Maybe";
            //            break;
            //        case DetectionResult.No:
            //            drawingText += " : No";
            //            break;
            //        case DetectionResult.Unknown:
            //            drawingText += " : Unknown";
            //            break;
            //        default:
            //            break;
            //        }
            //        offset += 30;
            //        formattedText = new FormattedText( drawingText, CultureInfo.GetCultureInfo( "ja-JP" ), FlowDirection.LeftToRight, new Typeface( "Georgia" ), 25, drawingBrush );
            //        drawingContext.DrawText( formattedText, new System.Windows.Point( box.Left, box.Bottom+offset ) );
            //    }
            //}

        }

        private void Reader_MultiSourceFrameArrived(object sender, MultiSourceFrameArrivedEventArgs e)
        {
            var reference = e.FrameReference.AcquireFrame();

            using (var colorFrame = reference.ColorFrameReference.AcquireFrame())
            using (var depthFrame = reference.DepthFrameReference.AcquireFrame())
            using (var bodyIndexFrame = reference.BodyIndexFrameReference.AcquireFrame())
            {
                if (colorFrame != null && depthFrame != null && bodyIndexFrame != null)
                {
                    // 3) Update the image source.
                    //Camera.Source = _bgRemovalHelper.removeBackground(colorFrame, depthFrame, bodyIndexFrame);
                    
                    //Camera.Source = _bgRemovalHelper.GreenScreen(colorFrame, depthFrame, bodyIndexFrame);
                    var bitmap = _bgRemovalHelper.GreenScreen(colorFrame, depthFrame, bodyIndexFrame);

                    Camera.Source = bitmap;
                }
            }
        }


        Grid g;
        private void switchPage(Grid preGrid, Grid nextGrid)//跳頁用的method
        {
            preGrid.Visibility = Visibility.Collapsed;//目前此頁設置隱藏
            g = nextGrid;//下一網格頁面覆蓋
            Canvas.SetLeft(g, 0);
            Canvas.SetTop(g, 0);
            nextGrid.Visibility = Visibility.Visible;
        }

        private void btnStart_Click(object sender, RoutedEventArgs e)
        {
            switchPage(Page1, Page2);
        }
        private void btnToPage3_Click_1(object sender, RoutedEventArgs e)
        {
            switchPage(Page2, Page3);
            image2.Source = image1.Source; // 風景背景
            Camera2.Source = Camera.Source;// 去背後的人體前景
        }

        private void btnToPage4_Click(object sender, RoutedEventArgs e)
        {
            switchPage(Page3, Page4);
            image3.Source = image2.Source; // 風景背景
            Camera3.Source = Camera2.Source; // 去背後的人體前景

        }
        private void bg1_Selected(object sender, RoutedEventArgs e)
        {
            image1.Source = s1.Source;

        }

        private void bg2_Selected(object sender, RoutedEventArgs e)
        {
            image1.Source = s2.Source;
        }
        private void bg3_Selected(object sender, RoutedEventArgs e)
        {
            image1.Source = s3.Source;
        }
        private void bg4_Selected(object sender, RoutedEventArgs e)
        {
            image1.Source = s4.Source;
        }

        private void seg5_Selected(object sender, RoutedEventArgs e)
        {
            image1.Source = s5.Source;
        }

        private void seg6_Selected(object sender, RoutedEventArgs e)
        {
            image1.Source = s6.Source;
        }

        
        private void seg2_1_Selected(object sender, RoutedEventArgs e)
        {
            object1 = true;
            object2 = false;
            object3 = false;
            object4 = false;
            object5 = false;
            object6 = false;
        }

        private void border1_Selected(object sender, RoutedEventArgs e)
        {

        }

        private void border2_Selected(object sender, RoutedEventArgs e)
        {

        }

        private void border3_Selected(object sender, RoutedEventArgs e)
        {

        }

        private void border4_Selected(object sender, RoutedEventArgs e)
        {

        }

        private void border5_Selected(object sender, RoutedEventArgs e)
        {

        }

        private void border6_Selected(object sender, RoutedEventArgs e)
        {

        }

        private void seg2_2_Selected(object sender, RoutedEventArgs e)
        {
            object1 = false;
            object2 = true;
            object3 = false;
            object4 = false;
            object5 = false;
            object6 = false;
        }
        private void seg2_3_Selected(object sender, RoutedEventArgs e)
        {
            object1 = false;
            object2 = false;
            object3 = true;
            object4 = false;
            object5 = false;
            object6 = false;
        }

        private void seg2_4_Selected(object sender, RoutedEventArgs e)
        {
            object1 = false;
            object2 = false;
            object3 = false;
            object4 = true;
            object5 = false;
            object6 = false;
        }

        private void seg2_5_Selected(object sender, RoutedEventArgs e)
        {
            object1 = false;
            object2 = false;
            object3 = false;
            object4 = false;
            object5 = true;
            object6 = false;
        }

        private void seg2_6_Selected(object sender, RoutedEventArgs e)
        {
            object1 = false;
            object2 = false;
            object3 = false;
            object4 = false;
            object5 = false;
            object6 = true;
        }

        
    }
    
}

