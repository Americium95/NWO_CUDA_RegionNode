using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NWO_RegionNode
{
    public static class Terrain
    {
        //충돌검사
        public static float terrainCollision(int x, int y)
        {
            x -= 256 * 5 + 90;
            y -= 256 * 5 + 90;
            string dir = @"E:\NWO\\NWOMAP2\" + (x / 2560 + 3) + "," + (y / 2560 + 3) + ".png";
            if (File.Exists(dir))
            {
                Bitmap bitmap = new Bitmap(dir);

                //Console.WriteLine(bitmap.GetPixel((x%2560)/50, (y % 2560) / 50));

                Color Rgb = bitmap.GetPixel((x % 2560) / 5, (y % 2560) / 5);

                float height = (-10000 + (((Rgb.R << 16) | (Rgb.G << 8) | Rgb.B) * 0.1f)) * (0.15f) * 1.25f;

                bitmap.Dispose();

                return height;
            }
            return 0;
        }
    }
}
