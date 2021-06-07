using Sandbox;
using System.Collections.Generic;

[Library]
public partial class PlayerController : BasePlayerController {
    [Net, Predicted] public bool IsInSprint {get; set;}
    [Net, Predicted] public bool IsAiming {get; set;}
    [Net, Predicted] public bool IsLeaning {get; set;}
    [Net, Predicted] public bool IsCrouching {get; set;}
	[Net, Predicted] public Vector3 LeanDirection { get; set; }
	[Net, Predicted] public float LeanDistance { get; set; }
	[Net, Predicted] public Vector3 LeanNormal { get; set; }
	[Net, Predicted] public Vector3 LeanPos { get; set; }
	[Net, Predicted] public float Lean { get; set; }

	public struct LeanResult {
		public bool valid;
		public Vector3 direction;
		public Vector3 normal;
		public Vector3 position;
		public float distance;
	}

    public override void Simulate( ) 
	{
        IsInSprint = Input.Down( InputButton.Run ) == true;

        var wantsAim = Input.Down( InputButton.Attack2 ) == true && !IsInSprint;

        if (wantsAim != IsAiming) {
            if (wantsAim) {
				var leanSurfaceCheck = CheckLeanSurface(out LeanResult leanResult);

				if (leanSurfaceCheck) {
					IsLeaning = true;

					LeanDirection = leanResult.direction;
					LeanNormal = leanResult.normal;
					LeanDistance = leanResult.distance;
					LeanPos = leanResult.position;
				}
            } else {
		        IsLeaning = false;
            }
        }

		if (IsLeaning) {
			var distanceFromLeanSurface = LeanPos.Distance(Input.Position);
			var surfaceDot = LeanNormal.Dot(-Input.Rotation.Forward);

			IsLeaning = IsLeaning && distanceFromLeanSurface < 50f;
			IsLeaning = IsLeaning && surfaceDot > 0.5f;
		}
		
		IsAiming = wantsAim;
    }

	public virtual void UpdateLean () {
		float leanAngle = LeanDistance * 2f;

		// Camera lean
		Lean = Lean.LerpTo(IsLeaning ? leanAngle : 0f, Time.Delta * 8.0f );
		EyePosLocal += LeanDirection * Lean;

		var ply = Pawn as  HeistPlayer;

		var boneId = ply.GetBoneIndex("head");
		var boneTx = ply.GetBoneTransform(boneId, false);

		boneTx.Rotation *= Rotation.From( 0, 0, -Lean * LeanDirection.y * 10f);

		var newTransform = new Transform {
			Position = boneTx.Position,
			Rotation = boneTx.Rotation * Rotation.From( 0, 0, -Lean * LeanDirection.y * 0.5f),
			Scale = boneTx.Scale
		};

		ply.SetBoneTransform(boneId, newTransform);
	}

    public virtual bool CheckLeanSurface (out LeanResult leanResult) {
		var centerTrace = Trace.Ray(Pawn.EyePos, Pawn.EyePos + Pawn.EyeRot.Forward * 50f)
							.WorldAndEntities()
							.Ignore(Pawn)
							.Size(3f)
							.Run();

		if (!centerTrace.Hit || centerTrace.Normal.Dot(-Pawn.EyeRot.Forward) < 0.7f) {
			leanResult = default;

			return false;
		}

		var directions = new List<Vector3> () {
			Vector3.Left,
			Vector3.Right
		};

		if (IsCrouching) {
			directions.Add(Vector3.Up);
		}

		var leanRadius = 15f;
		var leanMaxDepth = 12f;
		var closestSurfaceDistance = float.MaxValue;
		var surfaceDirection = Vector3.Up;
		var planarChecks = 1f;
		var hasValidSurface = false;

		for (int i = 0; i < directions.Count; i++) {
			var dir = directions[i];
			var planarDirection = Vector3.VectorPlaneProject(Pawn.EyeRot * dir, centerTrace.Normal).Normal;

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
									.Size(3f)
									.Run();

				if (!normalTrace.Hit) break;
			}

			var isValid = !normalTrace.Hit && planarTrace.Direction.Dot(Pawn.EyeRot.Forward) > -0.2f;
			var planarDistance = planarTrace.StartPos.Distance(normalTrace.StartPos);
	
			if (isValid && planarDistance < closestSurfaceDistance) {
				closestSurfaceDistance = planarDistance;
				surfaceDirection = dir;

				hasValidSurface = true;
			} 
		}

		leanResult = new LeanResult() {
			direction = surfaceDirection,
			normal = centerTrace.Normal,
			distance = closestSurfaceDistance,
			position = centerTrace.EndPos
		};

		return hasValidSurface;
	}
}
