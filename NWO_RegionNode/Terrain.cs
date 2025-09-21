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
                using (Bitmap bitmap = new Bitmap(dir))
                {
                    int px = (x % 2560) / 5;
                    int py = (y % 2560) / 5;

                    float sumHeight = 0f;
                    int count = 0;

                    // 3x3 영역 루프
                    for (int dy = -2; dy <= 2; dy++)
                    {
                        for (int dx = -2; dx <= 2; dx++)
                        {
                            int nx = px + dx;
                            int ny = py + dy;

                            // 범위 체크 (이미지 크기 벗어나지 않도록)
                            if (nx >= 0 && nx < bitmap.Width && ny >= 0 && ny < bitmap.Height)
                            {
                                Color rgb = bitmap.GetPixel(nx, ny);
                                float height = (-10000 + (((rgb.R << 16) | (rgb.G << 8) | rgb.B) * 0.1f)) * 0.15f * 1.25f;
                                height = height > 1 ? height : -1;
                                sumHeight += height;
                                count++;
                            }
                        }
                    }

                    if (count > 0)
                        return sumHeight / count; // 평균값
                }
            }
            return 0;
        }
    }
}
