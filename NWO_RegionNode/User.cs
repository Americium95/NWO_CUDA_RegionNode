using System.Numerics;


public class User
{
    public int id = 0;
    public Vector3 position = new Vector3(0, 0, 0);
    public int speed = 0;
    public int rot = 0;

    public User(int id, Vector3 position,int spd, int rot)
    {
        this.id = id;
        this.position = position;
        this.speed = spd;
        this.rot = rot;
    }
        
}