using System;
using System.Collections.Generic;
using OpenTK;
using OsuEnlightenOverlay.Gameplay.Beatmap;

namespace OsuEnlightenOverlay.Gameplay.HitObjects
{
    /// <summary>
    /// 선분 — osu! stable Graphics/Primitives/Line.cs 포팅.
    /// </summary>
    internal class Line
    {
        public Vector2 p1;
        public Vector2 p2;
        public bool forceEnd;
        public bool straight;

        public float Rho { get { return (p2 - p1).Length; } }

        public Line(Vector2 p1, Vector2 p2)
        {
            this.p1 = p1;
            this.p2 = p2;
        }
    }

    /// <summary>
    /// 커브 계산 — osu! stable SliderOsu.cs UpdateCalculations + OsuMathHelper 포팅.
    /// Bezier / Linear / PerfectCurve / Catmull 지원.
    /// 추측 없이 소스코드 그대로 포팅.
    /// </summary>
    internal static class SliderCurve
    {
        const int SLIDER_DETAIL_LEVEL = 50; // General.SLIDER_DETAIL_LEVEL
        const double Pi = Math.PI;

        /// <summary>
        /// 커브 경로 복제. 캐시는 BasePosition 기준이고, 인스턴스는 스택으로 옮길 수 있다.
        /// </summary>
        public static List<Line> ClonePath(List<Line> src)
        {
            if (src == null || src.Count == 0)
                return new List<Line>();
            List<Line> dst = new List<Line>(src.Count);
            for (int i = 0; i < src.Count; i++)
            {
                Line s = src[i];
                Line d = new Line(s.p1, s.p2);
                d.forceEnd = s.forceEnd;
                d.straight = s.straight;
                dst.Add(d);
            }
            return dst;
        }

        /// <summary>
        /// 컨트롤 포인트들로부터 커브 경로(List<Line>) 생성.
        /// osu! stable SliderOsu.UpdateCalculations 포팅.
        /// spatialLength에 도달하면 나머지 제어점/세그먼트는 테셀레이트하지 않는다.
        /// 트림 결과는 전체 테셀 후 자르는 것과 같고, 스크리블 슬라이더의 미사용 꼬리만 건너뛴다.
        /// </summary>
        public static List<Line> CalculateCurve(List<Vector2> controlPoints, CurveTypes curveType, double spatialLength)
        {
            List<Line> path = new List<Line>();

            if (controlPoints == null || controlPoints.Count == 0)
                return path;

            double lengthBudget = spatialLength > 0 ? spatialLength : double.MaxValue;
            double total = 0;

            switch (curveType)
            {
                case CurveTypes.Catmull:
                    CalculateCatmull(controlPoints, path, lengthBudget, ref total);
                    break;
                case CurveTypes.Bezier:
                    CalculateBezier(controlPoints, path, lengthBudget, ref total);
                    break;
                case CurveTypes.PerfectCurve:
                    CalculatePerfect(controlPoints, path, lengthBudget, ref total);
                    break;
                case CurveTypes.Linear:
                    CalculateLinear(controlPoints, path, lengthBudget, ref total);
                    break;
            }

            // SpatialLength에 맞춰 경로 트림/연장
            if (path.Count > 0 && spatialLength > 0)
            {
                total = 0;
                foreach (Line l in path)
                    total += l.Rho;

                // stable UpdateCalculations(SliderOsu.cs:704-723)와 동일하게 excess 부호 무관 처리:
                // total>spatialLength면 마지막 선분을 트림, total<spatialLength면 |excess|만큼 연장한다.
                // 예전엔 total>spatialLength 가드로 트림만 해서 '과길이' 슬라이더(제어점 경로<pixelLength)가
                // stable보다 짧게 그려지고 볼 타이밍/끝 위치가 어긋났다 (I-감사 #11). 루프 본문은 stable과 동일.
                {
                    double excess = total - spatialLength;
                    const float MIN_SEGMENT_LENGTH = 0.0001f;

                    while (path.Count > 0)
                    {
                        Line lastLine = path[path.Count - 1];
                        float lastLineLength = (lastLine.p2 - lastLine.p1).Length;

                        if (lastLineLength > excess + MIN_SEGMENT_LENGTH)
                        {
                            if (lastLine.p2 != lastLine.p1)
                            {
                                Vector2 dir = lastLine.p2 - lastLine.p1;
                                dir.Normalize();
                                lastLine.p2 = lastLine.p1 + dir * (lastLine.Rho - (float)excess);
                            }
                            break;
                        }

                        path.RemoveAt(path.Count - 1);
                        excess -= lastLineLength;
                    }
                }
            }

            return path;
        }

        /// <summary>
        /// 누적 길이 리스트 생성.
        /// </summary>
        public static List<double> CalculateCumulativeLengths(List<Line> path)
        {
            List<double> cumulative = new List<double>(path.Count);
            double total = 0;
            foreach (Line l in path)
            {
                total += l.Rho;
                cumulative.Add(total);
            }
            return cumulative;
        }

        // (PositionAt 제거 — 호출자가 없는 죽은 코드였고 이진탐색 뒤 lo-- 로 잘못된 세그먼트를
        //  가리키는 버그도 있었음, I-감사 #21. 실제 볼 위치는 SliderOsu.PositionAtLength가 계산한다.)

        // ── Catmull Rom ──

        static bool AddPathLine(List<Line> path, Line line, double lengthBudget, ref double total)
        {
            path.Add(line);
            total += line.Rho;
            return lengthBudget < double.MaxValue && total >= lengthBudget;
        }

        static void CalculateCatmull(List<Vector2> points, List<Line> path, double lengthBudget, ref double total)
        {
            for (int j = 0; j < points.Count - 1; j++)
            {
                Vector2 v1 = (j - 1 >= 0 ? points[j - 1] : points[j]);
                Vector2 v2 = points[j];
                Vector2 v3 = (j + 1 < points.Count ? points[j + 1] : v2 + (v2 - v1));
                Vector2 v4 = (j + 2 < points.Count ? points[j + 2] : v3 + (v3 - v2));

                for (int k = 0; k < SLIDER_DETAIL_LEVEL; k++)
                {
                    Line seg = new Line(
                        CatmullRom(v1, v2, v3, v4, (float)k / SLIDER_DETAIL_LEVEL),
                        CatmullRom(v1, v2, v3, v4, (float)(k + 1) / SLIDER_DETAIL_LEVEL));
                    if (AddPathLine(path, seg, lengthBudget, ref total))
                    {
                        path[path.Count - 1].forceEnd = true;
                        return;
                    }
                }
                path[path.Count - 1].forceEnd = true;
            }
        }

        static Vector2 CatmullRom(Vector2 v1, Vector2 v2, Vector2 v3, Vector2 v4, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * (
                (2f * v2) +
                (-v1 + v3) * t +
                (2f * v1 - 5f * v2 + 4f * v3 - v4) * t2 +
                (-v1 + 3f * v2 - 3f * v3 + v4) * t3);
        }

        // ── Bezier ──

        static void CalculateBezier(List<Vector2> points, List<Line> path, double lengthBudget, ref double total)
        {
            int lastIndex = 0;

            for (int i = 0; i < points.Count; i++)
            {
                // multipart segment: 연속된 동일 점으로 분할
                bool multipartSegment = i < points.Count - 2 && points[i] == points[i + 1];

                if (multipartSegment || i == points.Count - 1)
                {
                    List<Vector2> thisLength = new List<Vector2>();
                    for (int k = lastIndex; k <= i; k++)
                        thisLength.Add(points[k]);

                    if (thisLength.Count == 2)
                    {
                        // 2점은 선형
                        Line l = new Line(thisLength[0], thisLength[1]);
                        l.straight = true;
                        if (AddPathLine(path, l, lengthBudget, ref total))
                        {
                            path[path.Count - 1].forceEnd = true;
                            return;
                        }
                    }
                    else if (thisLength.Count > 2)
                    {
                        float remain = lengthBudget < double.MaxValue
                            ? (float)Math.Max(0, lengthBudget - total)
                            : float.MaxValue;
                        List<Vector2> bezierPoints = CreateBezier(thisLength, remain);
                        for (int j = 1; j < bezierPoints.Count; j++)
                        {
                            if (AddPathLine(path, new Line(bezierPoints[j - 1], bezierPoints[j]), lengthBudget, ref total))
                            {
                                path[path.Count - 1].forceEnd = true;
                                return;
                            }
                        }
                    }

                    if (path.Count > 0)
                        path[path.Count - 1].forceEnd = true;

                    if (multipartSegment) i++;
                    lastIndex = i;
                }
            }
        }

        /// <summary>
        /// BezierApproximator — osu! stable OsuMathHelper.BezierApproximator 정확 포팅.
        /// subdivisionBuffer1, subdivisionBuffer2 사용.
        /// </summary>
        static List<Vector2> CreateBezier(List<Vector2> input, float maxLength)
        {
            int count = input.Count;
            if (count == 0) return new List<Vector2>();
            if (count == 1) return new List<Vector2>(input);

            const float TOLERANCE = 0.5f;
            const float TOLERANCE_SQ = TOLERANCE * TOLERANCE;

            Vector2[] subdivisionBuffer1 = new Vector2[count];
            Vector2[] subdivisionBuffer2 = new Vector2[count * 2 - 1];

            List<Vector2> output = new List<Vector2>();

            Stack<Vector2[]> toFlatten = new Stack<Vector2[]>();
            Stack<Vector2[]> freeBuffers = new Stack<Vector2[]>();

            toFlatten.Push(input.ToArray());
            Vector2[] leftChild = subdivisionBuffer2;
            float acc = 0;
            Vector2 lastOut = input[0];
            bool hasOut = false;

            while (toFlatten.Count > 0)
            {
                Vector2[] parent = toFlatten.Pop();

                // IsFlatEnough
                bool flatEnough = true;
                for (int i = 1; i < parent.Length - 1; i++)
                {
                    Vector2 v = parent[i - 1] - 2 * parent[i] + parent[i + 1];
                    if (v.LengthSquared > TOLERANCE_SQ)
                    {
                        flatEnough = false;
                        break;
                    }
                }

                if (flatEnough)
                {
                    // Approximate — osu! stable과 정확히 일치
                    // Subdivide 호출
                    Vector2[] midpoints = subdivisionBuffer1;
                    for (int i = 0; i < count; ++i)
                        midpoints[i] = parent[i];

                    Vector2[] l = subdivisionBuffer2;
                    Vector2[] r = subdivisionBuffer1;

                    for (int i = 0; i < count; i++)
                    {
                        l[i] = midpoints[0];
                        r[count - i - 1] = midpoints[count - i - 1];
                        for (int j = 0; j < count - i - 1; j++)
                            midpoints[j] = (midpoints[j] + midpoints[j + 1]) / 2;
                    }

                    // l의 뒷부분에 r의 값 복사
                    for (int i = 0; i < count - 1; ++i)
                        l[count + i] = r[i + 1];

                    // output에 점 추가
                    if (AddBezierOutput(output, parent[0], maxLength, ref acc, ref lastOut, ref hasOut))
                        return output;
                    for (int i = 1; i < count - 1; ++i)
                    {
                        int index = 2 * i;
                        Vector2 p = 0.25f * (l[index - 1] + 2 * l[index] + l[index + 1]);
                        if (AddBezierOutput(output, p, maxLength, ref acc, ref lastOut, ref hasOut))
                            return output;
                    }

                    freeBuffers.Push(parent);
                    continue;
                }

                // Subdivide — 분할
                Vector2[] rightChild = freeBuffers.Count > 0 ? freeBuffers.Pop() : new Vector2[count];

                Vector2[] mids = subdivisionBuffer1;
                for (int i = 0; i < count; ++i)
                    mids[i] = parent[i];

                for (int i = 0; i < count; i++)
                {
                    leftChild[i] = mids[0];
                    rightChild[count - i - 1] = mids[count - i - 1];
                    for (int j = 0; j < count - i - 1; j++)
                        mids[j] = (mids[j] + mids[j + 1]) / 2;
                }

                // parent 버퍼 재사용
                for (int i = 0; i < count; ++i)
                    parent[i] = leftChild[i];

                toFlatten.Push(rightChild);
                toFlatten.Push(parent);
            }

            AddBezierOutput(output, input[count - 1], maxLength, ref acc, ref lastOut, ref hasOut);
            return output;
        }

        static bool AddBezierOutput(List<Vector2> output, Vector2 p, float maxLength, ref float acc, ref Vector2 last, ref bool hasOut)
        {
            if (hasOut && maxLength < float.MaxValue)
            {
                acc += (p - last).Length;
                output.Add(p);
                last = p;
                return acc >= maxLength;
            }
            output.Add(p);
            last = p;
            hasOut = true;
            return false;
        }

        // ── Perfect Curve (원형) ──

        static void CalculatePerfect(List<Vector2> points, List<Line> path, double lengthBudget, ref double total)
        {
            if (points.Count < 3)
            {
                CalculateLinear(points, path, lengthBudget, ref total);
                return;
            }
            if (points.Count > 3)
            {
                CalculateBezier(points, path, lengthBudget, ref total);
                return;
            }

            Vector2 a = points[0];
            Vector2 b = points[1];
            Vector2 c = points[2];

            // 일직선 체크
            if (IsStraightLine(a, b, c))
            {
                CalculateLinear(points, path, lengthBudget, ref total);
                return;
            }

            Vector2 centre;
            float radius;
            double tInitial, tFinal;

            CircleThroughPoints(a, b, c, out centre, out radius, out tInitial, out tFinal);

            double curveLength = Math.Abs((tFinal - tInitial) * radius);
            double needed = curveLength;
            if (lengthBudget < double.MaxValue)
                needed = Math.Min(curveLength, Math.Max(0, lengthBudget - total));
            int segments = (int)(needed * 0.125f);
            if (segments < 1) segments = 1;

            Vector2 lastPoint = a;
            double span = curveLength > 0 ? needed / curveLength : 1;

            for (int i = 1; i < segments; i++)
            {
                double progress = span * ((double)i / (double)segments);
                double t = tFinal * progress + tInitial * (1 - progress);

                Vector2 newPoint = CirclePoint(centre, radius, t);
                if (AddPathLine(path, new Line(lastPoint, newPoint), lengthBudget, ref total))
                    return;

                lastPoint = newPoint;
            }

            Vector2 endPoint = needed >= curveLength - 1e-4 ? c : CirclePoint(centre, radius, tFinal * span + tInitial * (1 - span));
            AddPathLine(path, new Line(lastPoint, endPoint), lengthBudget, ref total);
        }

        static bool IsStraightLine(Vector2 a, Vector2 b, Vector2 c)
        {
            return (b.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (b.Y - a.Y) == 0.0f;
        }

        static void CircleThroughPoints(Vector2 A, Vector2 B, Vector2 C,
            out Vector2 centre, out float radius, out double tInitial, out double tFinal)
        {
            float D = 2 * (A.X * (B.Y - C.Y) + B.X * (C.Y - A.Y) + C.X * (A.Y - B.Y));
            float AMagSq = A.LengthSquared;
            float BMagSq = B.LengthSquared;
            float CMagSq = C.LengthSquared;

            centre = new Vector2(
                (AMagSq * (B.Y - C.Y) + BMagSq * (C.Y - A.Y) + CMagSq * (A.Y - B.Y)) / D,
                (AMagSq * (C.X - B.X) + BMagSq * (A.X - C.X) + CMagSq * (B.X - A.X)) / D);
            radius = (centre - A).Length;

            tInitial = CircleTAt(A, centre);
            double tMid = CircleTAt(B, centre);
            tFinal = CircleTAt(C, centre);

            while (tMid < tInitial) tMid += 2 * Pi;
            while (tFinal < tInitial) tFinal += 2 * Pi;
            if (tMid > tFinal)
                tFinal -= 2 * Pi;
        }

        static double CircleTAt(Vector2 pt, Vector2 centre)
        {
            return Math.Atan2(pt.Y - centre.Y, pt.X - centre.X);
        }

        static Vector2 CirclePoint(Vector2 centre, float radius, double t)
        {
            return new Vector2((float)(Math.Cos(t) * radius), (float)(Math.Sin(t) * radius)) + centre;
        }

        // ── Linear ──

        static void CalculateLinear(List<Vector2> points, List<Line> path, double lengthBudget, ref double total)
        {
            for (int i = 1; i < points.Count; i++)
            {
                Line l = new Line(points[i - 1], points[i]);
                l.straight = true;
                if (AddPathLine(path, l, lengthBudget, ref total))
                {
                    path[path.Count - 1].forceEnd = true;
                    return;
                }
                path[path.Count - 1].forceEnd = true;
            }
        }
    }
}