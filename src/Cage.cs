using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mirror;

public class Cage : NetworkBehaviour
{
    [SyncVar] public float activationCountdown = 3;
    [SyncVar] bool trapActive;
    [SyncVar] bool trapInUse;
    [SyncVar] uint occupant;
    public Collider2D trigger;
    public Collider2D border;
    public Vector3 trapOffset;
    public Animator anim;

    public GameObject frontSprite;
    public GameObject backSprite;

    public SpriteRenderer indicatorSprite;

    public GameObject trapPrefab;

    private GameObject currentTrap;

    public TutorialGameManager.TutorialEvent onTrapActivate;
    void Start()
    {
        //anim = GetComponent<Animator>();
    }

    // Update is called once per frame
    void Update()
    {
        if (NetworkServer.active)
        {
            if (activationCountdown > 0)
            {
                activationCountdown -= Time.deltaTime;
            }
            else if (!trapActive && !trapInUse)
            {
                trapActive = true;
                trigger.enabled = true;
                HideIndicator();
            }
            else if (trapInUse && NetworkIdentity.spawned.ContainsKey(occupant))
            {
                NetworkIdentity.spawned[occupant].transform.position = transform.position + trapOffset;
            }
        }

        if (indicatorSprite.gameObject.activeSelf)
        {
            indicatorSprite.color = new Color(indicatorSprite.color.r, indicatorSprite.color.g, indicatorSprite.color.b, indicatorSprite.color.a - Time.deltaTime / 3);
        }

        if (isServer && occupant != 0 && !NetworkIdentity.spawned.ContainsKey(occupant))
        {
            NetworkServer.Destroy(gameObject);
            if (currentTrap != null)
            {
                NetworkServer.Destroy(currentTrap);
            }
        }
    }

    [Server]
    void OnTriggerEnter2D(Collider2D collider)
    {
        if (trapActive && collider.GetComponent<PlayerController>() != null && !collider.GetComponent<PlayerController>().inCage)
        {
            if (onTrapActivate != null)
                onTrapActivate.Invoke();
            trapInUse = true;
            trapActive = false;
            occupant = collider.GetComponent<NetworkIdentity>().netId;
            SetActive();
            StartCoroutine(TrapPlayer(collider.GetComponent<PlayerController>()));
        }
    }

    [Server]
    IEnumerator TrapPlayer(PlayerController player)
    {
        player.canMove = false;
        player.stunned = true;
        player.inCage = true;
        player.StopPlayer();
        player.RPCSetPosition(transform.position + trapOffset);
        //player.anim.Play("Trip");

        //anim.Play("CageActivate");
        yield return new WaitForSeconds(1f);

        GameObject trapGame = Instantiate(trapPrefab);
        currentTrap = trapGame;
        trapGame.GetComponent<PuzzleGame>().player = player.netId;
        trapGame.GetComponent<PuzzleGame>().puzzleClear += PuzzleClear;

        NetworkServer.Spawn(trapGame, player.gameObject);
    }

    [Server]
    void PuzzleClear()
    {
        if (!NetworkIdentity.spawned[occupant].GetComponent<PlayerController>().IsFrozen())
        {
            NetworkIdentity.spawned[occupant].GetComponent<PlayerController>().canMove = true;
        }

        NetworkIdentity.spawned[occupant].GetComponent<PlayerController>().stunned = false;
        NetworkIdentity.spawned[occupant].GetComponent<PlayerController>().inCage = false;

        NetworkServer.Destroy(gameObject);
    }

    [ClientRpc]
    void HideIndicator()
    {
        indicatorSprite.gameObject.SetActive(false);
    }

    [ClientRpc]
    void SetActive()
    {
        backSprite.SetActive(true);
        frontSprite.SetActive(true);
        trigger.enabled = false;
        border.enabled = true;
    }
}