namespace GoldbergSharp;

public class CycleBuffer<T>
{
    private readonly T[] _buffer1;
    private readonly T[] _buffer2;
    private T[] _currentBuf;
    private T[] _copySrcBuf;

    public CycleBuffer(int capacity)
    {
        _buffer1 = new T[capacity];
        _buffer2 = new T[capacity];
        _currentBuf = _buffer1;
        _copySrcBuf = _buffer2;
    }

    public void Push(T value)
    {
        _currentBuf[1..].CopyTo(_copySrcBuf, 0);
        _copySrcBuf[^1] = value;
        (_currentBuf, _copySrcBuf) = (_copySrcBuf, _currentBuf);
    }

    public Memory<T> AsMemory()
    {
        return _currentBuf.AsMemory();
    }
}