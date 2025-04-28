public class SimpleCircularBuffer<T>
{
    private T[] _buffer;
    private int _head;
    private int _tail;
    private int _count;
    private int _capacity;

    public int Count => _count;

    public SimpleCircularBuffer(int capacity)
    {
        _capacity = capacity;
        _buffer = new T[capacity];
        _head = 0;
        _tail = 0;
        _count = 0;
    }

    public void Enqueue(T item)
    {
        if (_count == _capacity)
        {
            _tail = (_tail + 1) % _capacity;
        }
        else
        {
            _count++;
        }
        _buffer[_head] = item;
        _head = (_head + 1) % _capacity;
    }

    public T Dequeue()
    {
        if (_count == 0) throw new InvalidOperationException("Buffer is empty");

        T value = _buffer[_tail];
        _tail = (_tail + 1) % _capacity;
        _count--;
        return value;
    }
}