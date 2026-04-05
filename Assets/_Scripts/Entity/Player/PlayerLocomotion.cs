using System.Collections.Generic;
using UnityEngine;

public class PlayerLocomotion : MonoBehaviour
{
    [SerializeField] private PlayerBrain brain = null;

    [Header("Body Parts")]
    [SerializeField] private List<BodyPart> bodyParts = new();

    private readonly Dictionary<BodyPartType, BodyPart> bodyPartDict = new();
    public IReadOnlyDictionary<BodyPartType, BodyPart> BodyParts => bodyPartDict;

    private void Awake()
    {
        InitializeBodyPartDictionary();
    }

    private void FixedUpdate()
    {
        ApplyAnimatedPose();
    }

    private void InitializeBodyPartDictionary()
    {
        bodyPartDict.Clear();

        for (int i = 0; i < bodyParts.Count; i++)
        {
            BodyPart part = bodyParts[i];

            if (part == null)
                continue;

            if (part.Type == BodyPartType.Default)
            {
                Debug.LogWarning("BodyPartType.Default 는 실제 파츠로 사용하지 않는 것이 좋다.", part);
                continue;
            }

            if (bodyPartDict.ContainsKey(part.Type))
            {
                Debug.LogWarning($"중복 BodyPartType 감지: {part.Type}", part);
                continue;
            }

            bodyPartDict.Add(part.Type, part);
        }
    }

    private void ApplyAnimatedPose()
    {
        foreach (KeyValuePair<BodyPartType, BodyPart> pair in bodyPartDict)
        {
            BodyPart part = pair.Value;

            if (part == null)
                continue;

            if (part.Joint == null)
                continue;

            if (part.AnimatedBody == null)
                continue;

            part.Joint.targetRotation = part.AnimatedBody.localRotation;
        }
    }

    public bool TryGetBodyPart(BodyPartType type, out BodyPart bodyPart)
    {
        return bodyPartDict.TryGetValue(type, out bodyPart);
    }
}