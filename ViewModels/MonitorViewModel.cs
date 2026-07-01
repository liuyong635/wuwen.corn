using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace SeedCut.ViewModels
{
    /// <summary>
    /// 监测页面的ViewModel
    /// </summary>
    public class MonitorViewModel : INotifyPropertyChanged
    {
        #region 事件

        public event PropertyChangedEventHandler PropertyChanged;

        #endregion

        #region 私有字段

        // 种子信息
        private string _perimeter = "100mm";
        private string _area = "100mm²";
        private string _length = "30mm";
        private string _width = "10mm";

        // 视觉图像
        private BitmapImage _grabVisionImage;
        private BitmapImage _smallMaterialVisionImage;
        private BitmapImage _laserVisionImage;
        private BitmapImage _largeMaterialVisionImage;

        #endregion

        #region 属性

        /// <summary>
        /// 周长
        /// </summary>
        public string Perimeter
        {
            get => _perimeter;
            set
            {
                if (_perimeter != value)
                {
                    _perimeter = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 面积
        /// </summary>
        public string Area
        {
            get => _area;
            set
            {
                if (_area != value)
                {
                    _area = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 长度
        /// </summary>
        public string Length
        {
            get => _length;
            set
            {
                if (_length != value)
                {
                    _length = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 宽度
        /// </summary>
        public string Width
        {
            get => _width;
            set
            {
                if (_width != value)
                {
                    _width = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 抓取视觉图像
        /// </summary>
        public BitmapImage GrabVisionImage
        {
            get => _grabVisionImage;
            set
            {
                if (_grabVisionImage != value)
                {
                    _grabVisionImage = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 小料视觉图像
        /// </summary>
        public BitmapImage SmallMaterialVisionImage
        {
            get => _smallMaterialVisionImage;
            set
            {
                if (_smallMaterialVisionImage != value)
                {
                    _smallMaterialVisionImage = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 激光视觉图像
        /// </summary>
        public BitmapImage LaserVisionImage
        {
            get => _laserVisionImage;
            set
            {
                if (_laserVisionImage != value)
                {
                    _laserVisionImage = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 大料视觉图像
        /// </summary>
        public BitmapImage LargeMaterialVisionImage
        {
            get => _largeMaterialVisionImage;
            set
            {
                if (_largeMaterialVisionImage != value)
                {
                    _largeMaterialVisionImage = value;
                    OnPropertyChanged();
                }
            }
        }

        #endregion

        #region 构造函数

        public MonitorViewModel()
        {
            // TODO: 初始化默认图像或从相机服务获取图像
            InitializeDefaultImages();
        }

        #endregion

        #region 方法

        /// <summary>
        /// 初始化默认图像（占位图）
        /// </summary>
        private void InitializeDefaultImages()
        {
            // TODO: 这里可以加载默认的占位图片
            // 或者从相机服务实时获取图像
        }

        /// <summary>
        /// 更新种子信息
        /// </summary>
        public void UpdateSeedInfo(string perimeter, string area, string length, string width)
        {
            Perimeter = perimeter;
            Area = area;
            Length = length;
            Width = width;
        }

        /// <summary>
        /// 更新抓取视觉图像
        /// </summary>
        public void UpdateGrabVisionImage(BitmapImage image)
        {
            GrabVisionImage = image;
        }

        /// <summary>
        /// 更新小料视觉图像
        /// </summary>
        public void UpdateSmallMaterialVisionImage(BitmapImage image)
        {
            SmallMaterialVisionImage = image;
        }

        /// <summary>
        /// 更新激光视觉图像
        /// </summary>
        public void UpdateLaserVisionImage(BitmapImage image)
        {
            LaserVisionImage = image;
        }

        /// <summary>
        /// 更新大料视觉图像
        /// </summary>
        public void UpdateLargeMaterialVisionImage(BitmapImage image)
        {
            LargeMaterialVisionImage = image;
        }

        #endregion

        #region INotifyPropertyChanged

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }
}