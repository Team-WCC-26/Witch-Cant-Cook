using Protocol;
using Server;
using System;
using UnityEngine;

public class Door : MonoBehaviour
{
    [SerializeField] private Transform leftDoor;
    [SerializeField] private Transform rightDoor;

    [SerializeField] private float openAngle = 90f;
    [SerializeField] private float openSpeed = 2f;

    private bool isOpen;

    private Quaternion leftClosed;
    private Quaternion rightClosed;

    private Quaternion leftOpened;
    private Quaternion rightOpened;

    private void Awake()
    {
        leftClosed = leftDoor.localRotation;
        rightClosed = rightDoor.localRotation;

        leftOpened = leftClosed * Quaternion.Euler(0, -openAngle, 0);
        rightOpened = rightClosed * Quaternion.Euler(0, openAngle, 0);

    }

    private void Update()
    {
        Quaternion leftTarget =
            isOpen ? leftOpened : leftClosed;

        Quaternion rightTarget =
            isOpen ? rightOpened : rightClosed;

        leftDoor.localRotation =
            Quaternion.Lerp(
                leftDoor.localRotation,
                leftTarget,
                Time.deltaTime * openSpeed);

        rightDoor.localRotation =
            Quaternion.Lerp(
                rightDoor.localRotation,
                rightTarget,
                Time.deltaTime * openSpeed);
    }

    public void Open()
    {
        isOpen = true;
    }

    public void Close()
    {
        isOpen = false;
    }

    public void OpenImmediate()
    {
        isOpen = true;

        leftDoor.localRotation = leftOpened;
        rightDoor.localRotation = rightOpened;
    }

    public void CloseImmediate()
    {
        isOpen = false;

        leftDoor.localRotation = leftClosed;
        rightDoor.localRotation = rightClosed;
    }
}