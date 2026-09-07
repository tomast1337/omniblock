namespace OmniBlock.PathFinding;

internal class Path
{
    private int _count;
    private PathPoint[] _pathPoints = new PathPoint[1024];

    public void AddPoint(PathPoint point)
    {
        if (point.Index >= 0) throw new InvalidOperationException("OW KNOWS!");

        if (_count == _pathPoints.Length)
        {
            var newArray = new PathPoint[_count << 1];
            Array.Copy(_pathPoints, 0, newArray, 0, _count);
            _pathPoints = newArray;
        }

        _pathPoints[_count] = point;
        point.Index = _count;
        SiftUp(_count++);
    }

    public void ClearPath() => _count = 0;

    public PathPoint Dequeue()
    {
        var result = _pathPoints[0];
        _pathPoints[0] = _pathPoints[--_count];
        _pathPoints[_count] = null!;

        if (_count > 0) SiftDown(0);

        result.Index = -1;
        return result;
    }

    public void ChangeDistance(PathPoint point, float newDistance)
    {
        var oldDistance = point.DistanceToTarget;
        point.DistanceToTarget = newDistance;

        if (newDistance < oldDistance) SiftUp(point.Index);
        else SiftDown(point.Index);
    }

    private void SiftUp(int index)
    {
        var point = _pathPoints[index];
        var distance = point.DistanceToTarget;

        while (index > 0)
        {
            var parentIndex = (index - 1) >> 1;
            var parentNode = _pathPoints[parentIndex];

            if (distance >= parentNode.DistanceToTarget) break;

            _pathPoints[index] = parentNode;
            parentNode.Index = index;
            index = parentIndex;
        }

        _pathPoints[index] = point;
        point.Index = index;
    }

    private void SiftDown(int index)
    {
        var point = _pathPoints[index];
        var distance = point.DistanceToTarget;

        while (true)
        {
            var leftChildIndex = 1 + (index << 1);
            var rightChildIndex = leftChildIndex + 1;

            if (leftChildIndex >= _count) break;

            var leftChild = _pathPoints[leftChildIndex];
            var leftDistance = leftChild.DistanceToTarget;

            PathPoint? rightChild = null;

            var rightDistance = float.PositiveInfinity;

            if (rightChildIndex < _count)
            {
                rightChild = _pathPoints[rightChildIndex];
                rightDistance = rightChild.DistanceToTarget;
            }

            if (leftDistance < rightDistance)
            {
                if (leftDistance >= distance) break;

                _pathPoints[index] = leftChild;
                leftChild.Index = index;
                index = leftChildIndex;
            }
            else
            {
                if (rightDistance >= distance) break;

                _pathPoints[index] = rightChild!;
                rightChild!.Index = index;
                index = rightChildIndex;
            }
        }

        _pathPoints[index] = point;
        point.Index = index;
    }

    public bool IsPathEmpty() => _count == 0;
}
