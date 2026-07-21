using System;
using System.Numerics;

namespace AimAssistPlugin.Sdk.Input;

internal class InputManager
{
	private static Vector2 lastCursorPos = Vector2.Zero;

	private static Vector2 accumulatedOffset = Vector2.Zero;

	public static Vector2 GetLastCursorPosition()
	{
		return lastCursorPos;
	}

	public static Vector2 GetAccumulatedOffset()
	{
		return accumulatedOffset;
	}

	public static void SetLastCursorPosition(Vector2 value)
	{
		lastCursorPos = value;
	}

	public static void SetAccumulatedOffset(Vector2 value)
	{
		accumulatedOffset = value;
	}

	public static Vector2 Resync(Vector2 displacement, Vector2 offset, float resyncFactor)
	{
		if (offset.Length() <= float.Epsilon)
		{
			return offset;
		}
		Vector2 vector = displacement * resyncFactor;
		offset.X = ((offset.X > 0f) ? Math.Max(0f, (vector.X >= 0f) ? (offset.X - vector.X) : (offset.X + vector.X)) : Math.Min(0f, (vector.X <= 0f) ? (offset.X - vector.X) : (offset.X + vector.X)));
		offset.Y = ((offset.Y > 0f) ? Math.Max(0f, (vector.Y >= 0f) ? (offset.Y - vector.Y) : (offset.Y + vector.Y)) : Math.Min(0f, (vector.Y <= 0f) ? (offset.Y - vector.Y) : (offset.Y + vector.Y)));
		return offset;
	}
}
