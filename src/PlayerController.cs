using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering.Universal;
using Mirror;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class PlayerController : NetworkBehaviour, IInteractable
{
    public int rotationAngle;

    [SyncVar] private Vector3 syncedVelocity;

    private float moveX;
    private float moveY;
    private Rigidbody2D rb;
    [SyncVar] public float speedMult;
    public bool whacking;
    public float velSyncRate;

    private bool typing;

    public SyncList<uint> nearbyObjects = new SyncList<uint>();
    public Inventory inventory;
    [SyncVar] public uint holding;
    public GameObject highlighted;

    [SyncVar] private bool tripped = false;
    [SyncVar] private bool isFrozen = false;
    [SyncVar] public bool canMove = true;
    [SyncVar] public bool pushable = false;

    //Used for stats
    public bool stunned;

    private Animator anim;
    private SpriteRenderer sprite;

    [SyncVar] public string playerName;
    private Image playerLabel;
    public List<Sprite> labelSprites;
    private enum LabelColor { GREEN, RED, BLUE, CLEAR };

    public SceneManagerBase sceneManager;
    FixedJoystick joystick;

    [SyncVar] public bool inCage;

    public GameObject rock;
    public GameObject tripVine;
    public GameObject tripVineEnd;
    public GameObject cage;

    public PlayerStats stats;

    public List<Material> colors = new List<Material>();
    [SyncVar] public int colorIndex;

    void Awake()
    {
        //DontDestroyOnLoad(gameObject);
        
    }

    void Start()
    {
        anim = GetComponentInChildren<Animator>();
        sprite = GetComponentInChildren<SpriteRenderer>();
        inventory = GetComponent<Inventory>();
        rb = GetComponent<Rigidbody2D>();
        playerLabel = GetComponentInChildren<Image>();
        //Debug.Log(playerLabel);

        if (GetComponent<NetworkRoomPlayer>() != null)
            SetPlayerLabel(LabelColor.CLEAR, playerName);
        else if (playerLabel != null && labelSprites != null && GetComponent<Seeker>() != null)
            SetPlayerLabel(LabelColor.RED, playerName);
        else if (playerLabel != null && labelSprites != null && GetComponent<Hider>() != null)
            SetPlayerLabel(LabelColor.GREEN, playerName);
    }
    public override void OnStartServer()
    {
        if (stats == null)
        {
            stats = new PlayerStats();
            stats.playerName = playerName;
        }

        //This is problematic
        foreach (NetworkIdentity id in connectionToClient.clientOwnedObjects)
        {
            if (GameManager.gameManager.playerList.Contains(id.netId))
            {
                GameManager.gameManager.UpdateIdentity(id, GetComponent<NetworkIdentity>());
                return;
            }
        }
    }

    public override void OnStartLocalPlayer()
    {
        sceneManager = FindObjectOfType<SceneManagerBase>();
        if (sceneManager != null)
        {
            joystick = sceneManager.sceneJoystick;
            if (sceneManager.sceneCamera != null)
            {
                sceneManager.sceneCamera.SetTarget(transform);
            }
        }
        else
        {
            Debug.Log("Scene has no scene manager");
        }

        StartCoroutine(SyncVelocity());
    }
    
    [Server]
    public void StopPlayer()
    {
        rb.velocity *= 0;
        syncedVelocity *= 0;
    }

    IEnumerator SyncVelocity()
    {
        while(true)
        {
            yield return new WaitForSeconds(velSyncRate);
            if (NetworkClient.ready && (Mathf.Abs(rb.velocity.magnitude - syncedVelocity.magnitude) > .01f || Mathf.Abs(Vector2.Angle(Vector2.up, rb.velocity) - Vector2.Angle(Vector2.up, syncedVelocity)) > .01f))
            {
                CmdSyncVelocity(rb.velocity);
            }
        }
    }

    [Command]
    void CmdSyncVelocity(Vector3 vel)
    {
        syncedVelocity = vel;
    }

    // Update is called once per frame
    void Update()
    {
        if (isServer && isFrozen)
        {
            stats.timeFrozen += Time.deltaTime;
        }
        else if (isServer && stunned)
        {
            stats.timeStunned += Time.deltaTime;
        }

        GetComponentInChildren<SpriteRenderer>().material = colors[colorIndex];
        
        if (!isLocalPlayer)
        {
            rb.velocity = syncedVelocity;
            return;
        }

        if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null && EventSystem.current.currentSelectedGameObject.GetComponent<InputField>() != null)
        {
            typing = true;
        }
        else
        {
            typing = false;
        }

        if (canMove && !typing)
        {
            moveX = Input.GetAxis("Horizontal");
            moveY = Input.GetAxis("Vertical");

            if (joystick != null && joystick.Horizontal != 0 && joystick.Vertical != 0)
            {
                moveX = joystick.Horizontal;
                moveY = joystick.Vertical;
            }

            Vector2 inputVector = new Vector2(moveX, moveY);
            if (inputVector.magnitude > 1)
            {
                inputVector = inputVector.normalized;
            }

            rb.velocity = inputVector * speedMult;
            
            if (NetworkClient.ready)
            {
                CmdSyncVelocity(rb.velocity);
            }
            
            rotationAngle = GetDirection();

            if (Input.GetKeyDown(KeyCode.J))
            {
                CmdInteract();
            }
        }
        else if ((!canMove || typing) && !pushable)
        {
            rb.velocity *= 0;
        }

        Animate();

        //Host only: Make sure other players' names don't appear when they aren't visible
        if (NetworkServer.active)
        {
            foreach (NetworkConnection conn in NetworkServer.connections.Values)
            {
                if (conn.identity != null)
                {
                    PlayerController player = conn.identity.GetComponent<PlayerController>();
                    if (player.sprite != null)
                    {
                        if (!player.sprite.enabled && player.playerLabel.gameObject.activeSelf)
                        {
                            player.playerLabel.gameObject.SetActive(false);
                        }
                        else if (player.sprite.enabled && !player.playerLabel.gameObject.activeSelf)
                        {
                            player.playerLabel.gameObject.SetActive(true);
                        }
                    }
                }
            }
        }
    }

    public bool GetCanMove()
    {
        return canMove;
    }
    private int GetDirection()
    {
        if (moveY != 0 && moveX != 0)
        {
            rotationAngle = (int)Mathf.Round(((Mathf.Atan(moveY / moveX)) * 180 / Mathf.PI));
            if (moveX > 0)
            {
                if (moveY < 0)
                {
                    rotationAngle += 360;
                }
            }
            else if (moveX < 0)
            {
                if (moveY > 0)
                {
                    rotationAngle += 180;
                }
                else if (moveY < 0)
                {
                    rotationAngle += 180;
                }
            }
        }
        else if (moveX == 0 && moveY != 0)
        {
            if (moveY > 0)
                rotationAngle = 90;
            else
                rotationAngle = 270;
        }
        else if (moveY == 0 && moveX != 0)
        {
            if (moveX > 0)
                rotationAngle = 0;
            else
                rotationAngle = 180;
        }
        return rotationAngle;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (isLocalPlayer && other.gameObject.GetComponent<IInteractable>() != null)
        {
            if (NetworkClient.ready && other.GetComponent<NetworkIdentity>() != null && !nearbyObjects.Contains(other.GetComponent<NetworkIdentity>().netId))
            {
                CmdVerifyTriggerEnter(other.GetComponent<NetworkIdentity>().netId);
            }
        }
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (NetworkClient.ready && isLocalPlayer && other.gameObject.GetComponent<IInteractable>() != null)
        {
            if (other.GetComponent<NetworkIdentity>() != null && nearbyObjects.Contains(other.GetComponent<NetworkIdentity>().netId))
            {
                CmdVerifyTriggerExit(other.GetComponent<NetworkIdentity>().netId);
            }
        }
    }

    [Command]
    private void CmdVerifyTriggerExit(uint other)
    {
        if (nearbyObjects.Contains(other))
            nearbyObjects.Remove(other);
    }

    [Command]
    private void CmdVerifyTriggerEnter(uint other)
    {
        if (!nearbyObjects.Contains(other))
        {
            nearbyObjects.Add(other);
        }
    }

    [Server]
    /// <Summary> Trips the player. Only call from server.
    /// 
    public void Trip()
    {
        stunned = true;
        tripped = true;
        anim.Play("Trip");
        canMove = false;
        rb.velocity *= 0;
        RPCSetPlayerLabel("blue", "HIT");
        Debug.Log("Trip!");
        StartCoroutine(StunTimer("vine"));
    }

    [Server]
    /// <Summary> Trips the player. Only call from server.
    /// 
    public void RockStun()
    {
        stunned = true;
        tripped = true;
        anim.Play("Trip");
        canMove = false;
        rb.velocity *= 0;
        RPCSetPlayerLabel("blue", "HIT");
        Debug.Log("RockHit!");
        StartCoroutine(StunTimer("rock"));
    }

    [Server]
    /// <Summary> Trips the player. Only call from server.
    /// 
    public void StickStun()
    {
        stunned = true;
        tripped = true;
        anim.Play("Trip");
        canMove = false;
        rb.velocity *= 0;
        RPCSetPlayerLabel("blue", "HIT");
        Debug.Log("Trip!");
        StartCoroutine(StunTimer("stick"));
    }

    [Server]
    private IEnumerator StunTimer(string type)
    {
        float time = 0;
        switch (type)
        {
            case "stick":
                time = GameManager.gameManager.tripTime;
                break;
            case "rock":
                time = GameManager.gameManager.tripTime;
                break;
            case "vine":
                time = GameManager.gameManager.tripTime;
                break;
        }
        yield return new WaitForSeconds(time);

        if (!isFrozen)
        {
            canMove = true;
            stunned = false;
            tripped = false;

            if (GetComponent<Hider>() != null)
            {
                RPCSetPlayerLabel("green", "");
            }
            else if (GetComponent<Seeker>() != null)
            {
                RPCSetPlayerLabel("red", "");
            }
        }
    }

    void Animate()
    {
        if (!anim.GetCurrentAnimatorStateInfo(0).IsName("ThrowRight") && 
            !anim.GetCurrentAnimatorStateInfo(0).IsName("ThrowUp") && 
            !anim.GetCurrentAnimatorStateInfo(0).IsName("ThrowDown") && 
            !anim.GetCurrentAnimatorStateInfo(0).IsName("ThrowLeft") && !whacking)
        {
            if (rb.velocity.magnitude < .1f && !tripped)
            {
                if (rotationAngle > 45 && rotationAngle < 135)
                {
                    anim.Play("IdleUp");
                }
                else if (rotationAngle > 225 && rotationAngle < 315)
                {
                    anim.Play("IdleDown");
                }
                else if ((rotationAngle <= 45 && rotationAngle >= 0) || (rotationAngle >= 315 && rotationAngle <= 360))
                {
                    anim.Play("IdleRight");
                }
                else
                {
                    anim.Play("IdleLeft");
                }
            }
            else if (Vector2.Dot(rb.velocity.normalized, Vector2.up) >= .707)
            {
                anim.Play("Walk Up");
            }
            else if (Vector2.Dot(rb.velocity.normalized, Vector2.down) >= .707)
            {
                anim.Play("Walk Down");
            }
            else if (Vector2.Dot(rb.velocity.normalized, Vector2.left) >= .707)
            {
                anim.Play("Walk Left");
            }
            else if (Vector2.Dot(rb.velocity.normalized, Vector2.right) >= .707)
            {
                anim.Play("Walk Right");
            }
        }
    }

    [Command]
    public void CmdInteract()
    {
        if (connectionToClient.isReady)
        {
            Debug.Log("Player Wants to Interact");
            //Debug.Log(nearbyObjects.ToString());
            
            if (nearbyObjects.Count > 0)
            {
                List<GameObject> tempNearby = new List<GameObject>();
                foreach (uint obj in nearbyObjects)
                {
                    if (NetworkIdentity.spawned.ContainsKey(obj))
                        tempNearby.Add(NetworkIdentity.spawned[obj].gameObject);
                }

                tempNearby.Sort(nearbySort);

                //If the player is holding an object and the nearest object is in hand, interact with the next closest object
                if (holding != 0 && tempNearby[0] == NetworkIdentity.spawned[holding].gameObject && tempNearby.Count > 1)
                {
                    //If the object is both resource and attach point, i.e. a tree
                    if (tempNearby[1].GetComponent<ResourceSpawner>() != null && GetComponent<AttachPoint>() != null)
                    {
                        //Prioritize getting resources over attaching vine
                        if (tempNearby[1].GetComponent<ResourceSpawner>().resourcesStored == 0)
                        {
                            tempNearby[1].GetComponent<AttachPoint>().OnInteract(this);
                        }
                        else
                        {
                            tempNearby[1].GetComponent<ResourceSpawner>().OnInteract(this);
                        }
                    }
                    else
                    {
                        tempNearby[1].GetComponent<IInteractable>().OnInteract(this);
                    }
                }
                //If holding a vine and nothing nearby
                else if (holding != 0 && NetworkIdentity.spawned[holding].GetComponent<TripvineEnd>() != null)
                {
                    //Place vine end
                    NetworkIdentity.spawned[holding].GetComponent<TripvineEnd>().attachPoint = 0;
                    NetworkIdentity.spawned[holding].GetComponent<TripvineEnd>().owner = 0;
                    holding = 0;
                }
                else
                {
                    if (tempNearby[0].GetComponent<ResourceSpawner>() != null && tempNearby[0].GetComponent<ResourceSpawner>().resourcesStored > 0)
                    {
                        tempNearby[0].GetComponent<ResourceSpawner>().OnInteract(this);
                    }
                    else if (tempNearby[0].GetComponent<PlayerController>() && tempNearby[0].GetComponent<PlayerController>().IsFrozen())
                    {
                        tempNearby[0].GetComponent<PlayerController>().OnInteract(this);
                    }
                    else if (tempNearby[0].GetComponent<IInteractable>() != null)
                    {
                        tempNearby[0].GetComponent<IInteractable>().OnInteract(this);
                    }
                }
            }
        }
    }

    //Whack
    #region
    [Command]
    public void CmdWhack()
    {
        if (inventory.whackSticks > 0)
        {
            RPCWhack();
            inventory.UseItem("stick");
        }
    }

    [ClientRpc]
    void RPCWhack ()
    {
        whacking = true;
        canMove = false;
        rb.velocity *= 0;

        Vector2 aimDirection = new Vector2(Mathf.Cos(rotationAngle * Mathf.Deg2Rad), Mathf.Sin(rotationAngle * Mathf.Deg2Rad));

        if (Vector2.Dot(aimDirection, Vector2.left) >= .707)
        {
            GetComponentInChildren<WhackStickAnimEvent>().transform.localScale = new Vector3(-1, 1);
            anim.Play("WhackLeft");
        }
        else if (Vector2.Dot(aimDirection, Vector2.right) >= .707)
        {
            GetComponentInChildren<WhackStickAnimEvent>().transform.localScale = new Vector3(1, 1);
            anim.Play("WhackRight");
        }
        else if (Vector2.Dot(aimDirection, Vector2.up) >= .707)
        {
            GetComponentInChildren<WhackStickAnimEvent>().transform.localScale = new Vector3(1, 1);
            anim.Play("WhackUp");
        }
        else if (Vector2.Dot(aimDirection, Vector2.down) >= .707)
        {
            GetComponentInChildren<WhackStickAnimEvent>().transform.localScale = new Vector3(1, 1);
            anim.Play("WhackDown");
        }
        StartCoroutine(BackupCanMove());


    }
    #endregion

    [Command]
    public void CmdUseTripvine()
    {
        if (inventory.tripVines > 0)
        {
            GameObject obj = Instantiate(tripVine, transform.position, Quaternion.identity);
            GameObject end1 = Instantiate(tripVineEnd, new Vector3(transform.position.x + 1, transform.position.y), Quaternion.identity);
            GameObject end2 = Instantiate(tripVineEnd, new Vector3(transform.position.x, transform.position.y), Quaternion.identity);

            NetworkServer.Spawn(obj);
            NetworkServer.Spawn(end1);
            NetworkServer.Spawn(end2);

            obj.GetComponent<Tripvine>().end1 = end1.GetComponent<NetworkIdentity>().netId;
            obj.GetComponent<Tripvine>().end2 = end2.GetComponent<NetworkIdentity>().netId;

            inventory.UseItem("vine");
            Debug.Log("Used tripvine!");
        }
    }

    [Command]
    public void CmdUseCage()
    {
        if (inventory.cages > 0)
        {
            GameObject obj = Instantiate(cage, transform.position, Quaternion.identity);
           
            NetworkServer.Spawn(obj);
            Debug.Log("Used Cage!");
            inventory.UseItem("cage");
        }
    }

    [Command]
    public void CmdThrowRock(Vector3 pos)
    {
        if (inventory.throwRocks > 0)
        {
            if (Vector2.Dot(pos - transform.position, Vector2.left) >= .707)
            {
                anim.Play("ThrowLeft");
                Debug.Log("Throw left");
            }
            else if (Vector2.Dot(pos - transform.position, Vector2.right) >= .707)
            {
                anim.Play("ThrowRight");
                Debug.Log("Throw right");
            }
            else if (Vector2.Dot(pos - transform.position, Vector2.up) >= .707)
            {
                anim.Play("ThrowUp");
                Debug.Log("Throw up");
            }
            else if (Vector2.Dot(pos - transform.position, Vector2.down) >= .707)
            {
                anim.Play("ThrowDown");
                Debug.Log("Throw down");
            }

            Rock newRock = Instantiate(rock).GetComponent<Rock>();
            NetworkServer.Spawn(newRock.gameObject);
            newRock.target = pos;
            newRock.origin = transform.position;
            inventory.UseItem("rock");
        }   
    }

    [Command]
    public void CmdHitPlayer(GameObject player)
    {
        player.GetComponent<PlayerController>().StickStun();
    }

    [Command]
    public void CmdFinishWhack()
    {
        FinishWhack();
    }

    [ClientRpc]
    private void FinishWhack()
    {
        if (!isFrozen)
            canMove = true;
        StopCoroutine(BackupCanMove());
    }

    public IEnumerator BackupCanMove()
    {
        yield return new WaitForSeconds(.85F);
        if (!isFrozen)
        {
            canMove = true;
        }
    }

    private int nearbySort(GameObject obj1, GameObject obj2)
    {
        return (int)(Vector2.Distance(transform.position, obj1.transform.position) - Vector2.Distance(transform.position, obj2.transform.position));
    }

    public bool IsFrozen()
    {
        return isFrozen;
    }

    [Server]
    public void FreezePlayer()
    {
        rb.velocity *= 0;
        canMove = false;
        isFrozen = true;
        RPCFreezeLabel();
    }

    [ClientRpc]
    public void RPCFreezeLabel()
    {
        SetPlayerLabel(LabelColor.BLUE, "FROZEN");
    }

    [ClientRpc]
    public void RPCUnfreezePlayer()
    {
        SetPlayerLabel(LabelColor.GREEN, "HIDER");
    }

    [Command]
    public void CmdUnfreeze()
    {
        canMove = true;
        isFrozen = false;
        RPCUnfreezePlayer();
    }

    [Server]
    public void OnInteract(PlayerController interactor)
    {
        if (interactor != this && interactor.GetComponent<Hider>() != null && isFrozen)
        {
            RPCUnfreezePlayer();
            Debug.Log("UnFroze Player");
            //playerLabel.gameObject.SetActive(false);
        }
        else if (GameManager.gameManager.gameMode == GameManager.GameMode.ZOMBIES && interactor != this && !canMove && GetComponent<Seeker>() != null && GameManager.gameManager.seekers.Count > 1)
        {
            GameManager.gameManager.ChangeRole(connectionToClient, this, "hider");
        }
    }


    void SetPlayerLabel(LabelColor color, string text)
    {
        //playerLabel.gameObject.SetActive(true);

        if (GameManager.gameManager.hideRoles && !isLocalPlayer)
        {
            playerLabel.sprite = labelSprites[3];
        }
        else
        {
            switch (color)
            {
                case LabelColor.GREEN:
                    playerLabel.sprite = labelSprites[2];
                    break;
                case LabelColor.BLUE:
                    playerLabel.sprite = labelSprites[1];
                    break;
                case LabelColor.RED:
                    playerLabel.sprite = labelSprites[0];
                    break;
                case LabelColor.CLEAR:
                    playerLabel.sprite = labelSprites[3];
                    break;
            }
        }
        playerLabel.GetComponentInChildren<Text>().text = playerName;
    }

    [ClientRpc]
    void RPCSetPlayerLabel(string labelColor, string text)
    {
        LabelColor color = LabelColor.CLEAR;
        switch (labelColor)
        {
            case "red":
                color = LabelColor.RED;
                break;
            case "blue":
                color = LabelColor.BLUE;
                break;
            case "green":
                color = LabelColor.GREEN;
                break;
        }
        SetPlayerLabel(color, text);
    }

    /**
     * Use when canMove and position need to be set after one another, and setting position on server will be overwritten
     */
    [ClientRpc]
    public void RPCSetPosition(Vector3 pos)
    {
        transform.position = pos;
    }

    [Server]
    public void TakeResources(ResourceSpawner resource)
    {
        if (Vector2.Distance(resource.transform.position, transform.position) < 5)
        inventory.GiveResource(resource.resource, resource.resourcesStored);
        resource.resourcesStored = 0;
        Debug.Log("Resources Taken");
    }

    [Server]
    public void SetColor(int index)
    {
        GetComponentInChildren<SpriteRenderer>().material = colors[index];
        colorIndex = index;
    }
}