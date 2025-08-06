using UnityEngine;

public class AudioCuePlayer : MonoBehaviour
{
    public Transform audioHeading; // Empty object at world origin, rotates to face target
    public AudioSource audioSource; // Must have 3D spatial blend, attached to audioHeading
    public AudioClip[] distanceCues; // “Target 100m”, “Target 200m”, etc.

    public void PlayCueToward(Vector3 targetWorldPos, float distanceMeters)
    {
        // Step 1: Face the target by rotating audioHeading
        Vector3 direction = targetWorldPos - audioHeading.position;
        direction.y = 0f; // Flatten on Y-axis

        if (direction != Vector3.zero)
        {
            Quaternion rotation = Quaternion.LookRotation(direction);
            audioHeading.rotation = rotation;
        }

        // Step 2: Choose distance-based voice clip
        AudioClip clip = GetDistanceClip(distanceMeters);
        if (clip == null)
        {
            Debug.LogWarning("No clip found for distance: " + distanceMeters);
            return;
        }

        // Step 3: Play spatial cue
        audioSource.clip = clip;
        audioSource.Play();
    }

    private AudioClip GetDistanceClip(float distance)
    {
        if (distance < 150) return FindClip("100");
        if (distance < 250) return FindClip("200");
        return FindClip("300");
    }

    private AudioClip FindClip(string keyword)
    {
        foreach (var clip in distanceCues)
        {
            if (clip.name.Contains(keyword))
                return clip;
        }
        return null;
    }
}
