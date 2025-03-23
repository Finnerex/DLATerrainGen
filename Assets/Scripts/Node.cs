using System.Collections.Generic;
using UnityEngine;

public class Node
{
    public Node(float x, float y)
    {
        Fx = x;
        Fy = y;
    }

    public int X { get; private set; }

    public int Y { get; private set; }

    public float Fx
    {
        get => _fx;
        set {
            X = Mathf.RoundToInt(value);
            _fx = value;
        }
    }
    public float Fy
    {
        get => _fy;
        set {
            Y = Mathf.RoundToInt(value);
            _fy = value;
        }
    }
    

    private float _fy;
    private float _fx;


    public List<Node> Children = new();
}