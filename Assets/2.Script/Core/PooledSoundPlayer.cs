using UnityEngine;

public static class PooledSoundPlayer
{
    public static PooledSound Play(GameObject soundPrefab, AudioClip clip, Vector3 position, float volume = 1f, float pitch = 1f)
    {
        if (soundPrefab == null || clip == null)
            return null;

        PooledSound soundPrefabComponent = soundPrefab.GetComponent<PooledSound>();
        if (soundPrefabComponent == null)
        {
            Debug.LogWarning($"Sound prefab '{soundPrefab.name}' needs a PooledSound component.", soundPrefab);
            return null;
        }

        PooledSound sound = GameObjectPoolManager.Spawn(soundPrefabComponent, position, Quaternion.identity);
        if (sound == null)
            return null;

        sound.Play(clip, volume, pitch);
        return sound;
    }
}
