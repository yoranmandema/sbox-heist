using Sandbox;
using System;
using System.Linq;

partial class HeistViewModel : BaseViewModel
{
	float walkBob = 0;
	Vector3 velocity;
	Vector3 force; 
	Vector3 acceleration;
	float returnForce = 100f;
	float damping = 5f;
	float accelDamping = 1f;

	Vector3 offset = new Vector3(2f,0,10f);


	public override void PostCameraSetup( ref CameraSetup camSetup )
	{
		base.PostCameraSetup( ref camSetup );

		// camSetup.ViewModelFieldOfView = camSetup.FieldOfView + (FieldOfView - 80);

		AddCameraEffects( ref camSetup );
	}

	private void AddCameraEffects( ref CameraSetup camSetup )
	{
		//
		// Bob up and down based on our walk movement
		//
		var speed = Owner.Velocity.Length.LerpInverse( 0, 320 );
		var left = camSetup.Rotation.Left;
		var up = camSetup.Rotation.Up;
		var forward = camSetup.Rotation.Forward;

		if ( Owner.GroundEntity != null )
		{
			walkBob += Time.Delta * 15.0f * speed;
		}

		// acceleration += Vector3.Left * -Local.Client.Input.MouseDelta.z * Time.Delta;
		acceleration += Vector3.Up * -Local.Client.Input.MouseDelta.x * Time.Delta;
		acceleration += Vector3.Left * -Local.Client.Input.MouseDelta.y * Time.Delta;
		acceleration += -velocity * returnForce * Time.Delta;

		acceleration += Vector3.Up * MathF.Sin( walkBob ) * speed * -0.1f;
		acceleration += Vector3.Left * MathF.Sin( walkBob * 2f ) * speed * -0.1f;
		acceleration += Vector3.Forward * MathF.Sin( walkBob * 1f) * speed * 0.1f;

		velocity += acceleration * Time.Delta;

		ApplyDamping(ref acceleration, accelDamping );

		ApplyDamping( ref velocity, damping );

		Rotation = Local.Pawn.EyeRot;
		Rotation *= Rotation.FromYaw(velocity.x * 2f );
		Rotation *= Rotation.FromRoll(-velocity.x * 2f);
		Rotation *= Rotation.FromPitch(-velocity.y * 2f );

		Position += forward * (velocity.x * 10f+ offset.x);
		Position += left * (velocity.z* 10f + offset.z);
		Position += up * (velocity.y* 10f + offset.y);

		Position += (Rotation.Forward - camSetup.Rotation.Forward) * -1000f;

	}

	public void ApplyImpulse (Vector3 impulse) {
		acceleration += impulse;
	}

	private void ApplyDamping( ref float value, float damping )
	{
		if ( value != 0 )
		{
			var drop = value * damping * Time.Delta;
			value *= Math.Max( value - drop, 0 ) / value;
		}
	}

	private void ApplyDamping( ref Vector3 value, float damping )
	{
		var magnitude = value.Length;

		if ( magnitude != 0 )
		{
			var drop = magnitude * damping * Time.Delta;
			value *= Math.Max( magnitude - drop, 0 ) / magnitude;
		}
	}
}
