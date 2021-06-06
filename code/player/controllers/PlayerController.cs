using Sandbox;
using System.Collections.Generic;

[Library]
public partial class PlayerController : BasePlayerController {
    [Net, Predicted] public bool IsInSprint {get; set;}
    [Net, Predicted] public bool IsAiming {get; set;}
    [Net, Predicted] public bool IsLeaning {get; set;}
	[Net, Predicted] public Vector3 LeanDirection { get; set; }
	[Net, Predicted] public float LeanDistance { get; set; }
	[Net, Predicted] public Vector3 LeanNormal { get; set; }
	[Net, Predicted] public Vector3 LeanPos { get; set; }

    public override void Simulate( ) 
	{
        IsInSprint = Input.Down( InputButton.Run ) == true;


        var wantsAim = Input.Down( InputButton.Attack2 ) == true && !IsInSprint;

        var leanSurfaceCheck = CheckLeanSurface();

        if (wantsAim != IsAiming) {
            if (wantsAim && leanSurfaceCheck) {
			    IsLeaning = true;
            } else {
		        IsLeaning = false;
            }
        }

        // if (!leanSurfaceCheck) IsLeaning = false;

		if (IsLeaning) {
			var projectedLeanDir = Vector3.VectorPlaneProject(Input.Rotation * LeanDirection, LeanNormal).Normal;

			DebugOverlay.Line(LeanPos + LeanNormal, LeanPos + LeanNormal + projectedLeanDir * LeanDistance, Color.Yellow);
		}

		IsAiming = wantsAim;
    }

    public virtual bool CheckLeanSurface () {
		var centerTrace = Trace.Ray(Input.Position, Input.Position + Input.Rotation.Forward * 40f)
							.WorldAndEntities()
							.Ignore(Pawn)
							.Run();

		if (!centerTrace.Hit || centerTrace.Normal.Dot(-Input.Rotation.Forward) < 0.7f) return false;

		var directions = new List<Vector3> () {
			Vector3.Left,
			Vector3.Right,
			Vector3.Down,
			Vector3.Up
		};

		var leanRadius = 12f;
		var leanMaxDepth = 6f;
		var closestSurfaceDistance = float.MaxValue;
		var surfaceDirection = Vector3.Up;
		var planarChecks = 3f;
		var hasValidSurface = false;

		for (int i = 0; i < directions.Count; i++) {
			var dir = directions[i];
			var planarDirection = Vector3.VectorPlaneProject(Input.Rotation * dir, centerTrace.Normal).Normal;

			TraceResult planarTrace = Trace.Ray(centerTrace.EndPos + centerTrace.Normal, centerTrace.EndPos + centerTrace.Normal + planarDirection * leanRadius)
								.WorldAndEntities()
								.Ignore(Pawn)
								.Run();

			TraceResult normalTrace = default;

			for (int j = 0; j < planarChecks; j++) {
				var distance = leanRadius * ((j + 1) / planarChecks) * planarTrace.Fraction;
				var startPos = planarTrace.StartPos + planarTrace.Direction * distance;

				normalTrace = Trace.Ray(startPos, startPos - (centerTrace.Normal * leanMaxDepth))
									.WorldAndEntities()
									.Ignore(Pawn)
									.Run();

				if (!normalTrace.Hit) break;
			}

			var isValid = !normalTrace.Hit && planarTrace.Direction.Dot(Input.Rotation.Forward) > -0.1f;
			var planarDistance = planarTrace.StartPos.Distance(normalTrace.StartPos);
	
			if (isValid && planarDistance < closestSurfaceDistance) {
				closestSurfaceDistance = planarDistance;
				surfaceDirection = dir;

				hasValidSurface = true;
			} 
		}

		if (hasValidSurface) {
			LeanDirection = surfaceDirection;
			LeanNormal = centerTrace.Normal;
			LeanDistance = closestSurfaceDistance;
			LeanPos = centerTrace.EndPos;
		}

		return hasValidSurface;
	}
}