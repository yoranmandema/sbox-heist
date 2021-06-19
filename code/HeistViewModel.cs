using Sandbox;
using System;
using System.Linq;

partial class HeistViewModel : BaseViewModel
{
	[Net,Predicted] public BaseHeistWeapon Weapon {get; set;}

	float walkBob = 0;
	Vector3 velocity;
	Vector3 acceleration;
	float MouseScale => 1.5f;
	float ReturnForce => 400f;
	float Damping => 18f;
	float AccelDamping => 0.05f;
	float PivotForce => 1000f;
	float VelocityScale => 10f;
	float RotationScale => 2.5f;
	float LookUpPitchScale => 10f;
	float LookUpSpeedScale => 10f;
	float NoiseSpeed => 0.8f;
	float NoiseScale => 20f;
	public Vector3 OverallOffset {get; set; } = new Vector3( 0, 0, -0 );
	Vector3 WalkCycleOffsets => new Vector3( 50f, 20f, 20f );
	float ForwardBobbing => 4f;
	float SideWalkOffset => 80f;
	public Vector3 AimOffset {get; set;} =  new Vector3( 10f, 16.9f, 2.8f );
	Vector3 Offset => new Vector3( 1f, 13f, -8f );
	Vector3 CrouchOffset => new Vector3( -20f, -15f, -0f );
	Vector3 SmoothedVelocity;
	float VelocityClamp => 3f;

	float noiseZ = 0;
	float noisePos = 0;
	float upDownOffset = 0;
	float avoidance = 0;
	float avoidanceLeftDot;
	float avoidanceUpDot;
	
	Rotation avoidanceNormalRotation;

	float sprintLerp = 0;
	float aimLerp = 0;

	float smoothedDelta = 0;

	float DeltaTime => smoothedDelta;
	

	public HeistViewModel()
	{
		noiseZ = Rand.Float( -10000, 10000 );
	}

	public override void PostCameraSetup( ref CameraSetup camSetup )
	{
		base.PostCameraSetup( ref camSetup );


		AddCameraEffects( ref camSetup );
	}

	private void SmoothDeltaTime () {
		var delta = (Time.Delta - smoothedDelta) * Time.Delta;
		var clamped = MathF.Min(MathF.Abs(delta), 1/60f);

		smoothedDelta += clamped * MathF.Sign(delta);
	}

	private void AddCameraEffects( ref CameraSetup camSetup )
	{
		SmoothDeltaTime();

		SmoothedVelocity += (Owner.Velocity - SmoothedVelocity) * 5f * DeltaTime;

		// Should prevent near clipping, but is it a good idea?
		camSetup.ZNear = 1f;

		var camTransform = new Transform(Owner.EyePos, Owner.EyeRot);
		var speed = Owner.Velocity.Length.LerpInverse( 0, 320 );
		var bobSpeed = SmoothedVelocity.Length.LerpInverse( -100, 320 );
		var left = camSetup.Rotation.Left;
		var up = camSetup.Rotation.Up;
		var forward = camSetup.Rotation.Forward;
		var owner = Owner as HeistPlayer;
		var walkController = owner.Controller as WalkController;
		var avoidanceTrace = Trace.Ray( camSetup.Position, camSetup.Position + forward * 50f )
						.UseHitboxes()
						.Ignore( Owner )
						.Ignore( this )
						.Size(2)
						.Run();

		var desiredAvoidanceNormal = -forward;

		if (avoidanceTrace.Hit) {
			desiredAvoidanceNormal = avoidanceTrace.Normal;
		}

		var bobScale = Weapon.BobScale;
		var swayScale = Weapon.SwayScale;
		var bobScaleSqr = MathF.Pow( bobScale, 2 );
		var swayScaleSqr = MathF.Pow( swayScale, 2 );

		avoidanceNormalRotation = Rotation.Slerp(avoidanceNormalRotation, Rotation.LookAt(desiredAvoidanceNormal, Vector3.Up), 10 * Time.Delta);

		var avoidanceNormal = avoidanceNormalRotation.Forward;

		LerpTowards( ref avoidance, avoidanceTrace.Hit ? (1f - avoidanceTrace.Fraction) : 0, 4f );
		LerpTowards( ref sprintLerp, Weapon.IsInSprint ? 1 : 0, 8f );
		LerpTowards( ref aimLerp, Weapon.IsAiming ? 1 : 0, 12f );
		LerpTowards( ref upDownOffset, speed * -LookUpSpeedScale + camSetup.Rotation.Forward.z * -LookUpPitchScale * bobScale, LookUpPitchScale * bobScale );

		FieldOfView = 80f * (1- aimLerp) + 40f * aimLerp;

		bobSpeed *= (1 - sprintLerp * 0.25f);

		if ( Owner.GroundEntity != null )
		{
			walkBob += Time.Delta * 30.0f * bobSpeed;
		}

		if ( Owner.Velocity.Length < 60 )
		{
			var step = MathF.Round( walkBob / 90 );

			walkBob += (step * 90 - walkBob) * 10f * Time.Delta;
		}

		if ( walkController?.Duck?.IsActive == true )
		{
			acceleration += CrouchOffset * DeltaTime * (1-aimLerp);
		}

		walkBob %= 360;

		noisePos += DeltaTime * NoiseSpeed * swayScale;

		acceleration += Vector3.Left * -Input.MouseDelta.x * DeltaTime * MouseScale * 0.5f   * (1f-aimLerp * 3f) * swayScaleSqr;
		acceleration += Vector3.Up * -Input.MouseDelta.y * DeltaTime * MouseScale * (1f-aimLerp * 3f) * swayScaleSqr;
		acceleration += -velocity * ReturnForce * DeltaTime * bobScale;

		// Apply horizontal offsets based on walking direction
		var horizontalForwardBob = WalkCycle( 0.5f, 3f ) * speed * WalkCycleOffsets.x * DeltaTime * bobScaleSqr;

		acceleration += forward.WithZ( 0 ).Normal.Dot( Owner.Velocity.Normal ) * Vector3.Forward * ForwardBobbing * horizontalForwardBob;

		// Apply left bobbing and up/down bobbing
		acceleration += Vector3.Left * WalkCycle( 0.5f, 2f ) * speed * WalkCycleOffsets.y * (1 + sprintLerp) * DeltaTime * bobScaleSqr;
		acceleration += Vector3.Up * WalkCycle( 0.5f, 2f, true ) * speed * WalkCycleOffsets.z * DeltaTime * bobScaleSqr;

		acceleration += left.WithZ( 0 ).Normal.Dot( Owner.Velocity.Normal ) * Vector3.Left * speed * SideWalkOffset * DeltaTime * (1-aimLerp* 0.5f) * bobScaleSqr;

		// Scale movement to model scale
		velocity += acceleration * DeltaTime;

		ApplyDamping( ref acceleration, AccelDamping );

		ApplyDamping( ref velocity, Damping * (1 + aimLerp));

		acceleration += new Vector3(
			Noise.Perlin( noisePos, 0f, noiseZ ),
			Noise.Perlin( noisePos, 10f, noiseZ ),
			Noise.Perlin( noisePos, 20f, noiseZ )
		) * NoiseScale * Time.Delta * (1-aimLerp * 0.9f) * swayScale;

		velocity = velocity.Normal * Math.Clamp( velocity.Length, 0, VelocityClamp );

		Rotation desiredRotation = Local.Pawn.EyeRot;
		desiredRotation *= Rotation.FromAxis(Vector3.Up, velocity.y * RotationScale* (1-aimLerp));
		desiredRotation *= Rotation.FromAxis(Vector3.Forward, -velocity.y * RotationScale* (1-aimLerp * 0.0f) - 10f * (1-aimLerp));
		desiredRotation *= Rotation.FromAxis(Vector3.Right, velocity.z * RotationScale* (1-aimLerp));

		Rotation = desiredRotation; 

		var desiredOffset = Vector3.Lerp(Offset, AimOffset, aimLerp);

		Position += forward * (velocity.x * VelocityScale * bobScale + desiredOffset.x);
		Position += left * (velocity.y * VelocityScale * bobScale + desiredOffset.y);
		Position += up * (velocity.z * VelocityScale * bobScale + desiredOffset.z + upDownOffset * (1-aimLerp) * bobScale);

		Position += (desiredRotation.Forward - camSetup.Rotation.Forward) * -PivotForce;

		// Apply sprinting / avoidance offsets
		var offsetLerp = MathF.Max(sprintLerp, avoidance);

		var avoidanceUp = Vector3.VectorPlaneProject(Vector3.Up, avoidanceNormal).Normal;

		if (Vector3.Up.Dot(avoidanceNormal) > 0.5f) {
			avoidanceUp = -avoidanceNormal;
		}

		var avoidanceLeft = Vector3.VectorPlaneProject(left, avoidanceNormal).Normal;

		LerpTowards( ref avoidanceLeftDot, forward.Dot(avoidanceLeft), 4f );
		LerpTowards( ref avoidanceUpDot, forward.Dot(avoidanceUp) * (1f - 2 * MathF.Abs(-avoidanceLeftDot)), 4f );

		Rotation *= Rotation.FromAxis(Vector3.Up, velocity.y * (sprintLerp * 30f) + (sprintLerp + avoidance * avoidanceLeftDot * (1-sprintLerp)) * 50f * (1 - aimLerp) * bobScale);
		Rotation *= Rotation.FromAxis(Vector3.Right, avoidance * 50f * avoidanceUpDot  * (1-aimLerp) * bobScale);

		Position += forward * (sprintLerp * -10f + (MathF.Max(avoidance, avoidance * MathF.Max(MathF.Abs(avoidanceLeftDot),0.5f)) * -20f)) * bobScale;
		Position += left * ((velocity.y * -50f - 10) * sprintLerp + offsetLerp * 4f * -(avoidanceLeftDot + 0.25f)   * (1-aimLerp)) * bobScale;
		Position += up * (offsetLerp * -0f  + avoidance * avoidanceUpDot * -10 * (1-aimLerp)) * bobScale;

		Position += forward * OverallOffset.x + left * OverallOffset.y + up * OverallOffset.z;
	}

	private float WalkCycle( float speed, float power, bool abs = false )
	{
		var sin = MathF.Sin( walkBob * speed );
		var sign = Math.Sign( sin );

		if ( abs )
		{
			sign = 1;
		}

		return MathF.Pow( sin, power ) * sign;
	}

	public void ApplyImpulse( Vector3 impulse )
	{
		acceleration += impulse;
	}

	private void LerpTowards( ref float value, float desired, float speed )
	{
		var delta = (desired - value) * speed * DeltaTime;
		var deltaAbs = MathF.Min( MathF.Abs( delta ), MathF.Abs( desired - value ) ) * MathF.Sign( delta );

		if ( MathF.Abs( desired - value ) < 0.001f )
		{
			value = desired;

			return;
		}

		value += deltaAbs;
	}

	private void ApplyDamping( ref Vector3 value, float damping )
	{
		var magnitude = value.Length;

		if ( magnitude != 0 )
		{
			var drop = magnitude * damping * DeltaTime;
			value *= Math.Max( magnitude - drop, 0 ) / magnitude;
		}
	}
}
