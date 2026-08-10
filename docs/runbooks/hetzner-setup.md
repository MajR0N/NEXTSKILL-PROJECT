# Runbook — Setting up the Hetzner VPS

Follow top to bottom. Every step says **why**, not just what — the point is to understand
the box, not to paste commands.

**Target:** Hetzner **CAX11** (ARM, 2 vCPU, 4 GB, 40 GB NVMe), **Falkenstein**, Ubuntu LTS.
Decided in [042](../decisions/042-hosting.md).

---

## 0. Before you start

You need: a payment method (Hetzner asks even for small amounts), and a domain you control.

### ⚠️ Pick the right Hetzner product

Hetzner sells four things with confusingly similar names. **Only one of them can run this app.**

| Product | What it is | Verdict |
| --- | --- | --- |
| **Hetzner Webhosting** | Shared hosting — PHP + MySQL, fixed software, no root | ❌ **Cannot run .NET.** Not this one |
| **Hetzner Cloud** → `console.hetzner.cloud` | A VPS: your own Linux machine, root access | ✅ **This one.** CAX11 lives here |
| Hetzner Dedicated (Robot) | Bare metal | Overkill, ~€40+/month |
| Hetzner Storage Box | Bulk storage, no compute | Useful later as an off-host backup target (step 10) |

> *Plain version:* **shared hosting** is a folder on someone else's machine with a fixed menu
> of software — almost always PHP and MySQL. You cannot install anything. **A VPS** is the
> whole (virtual) machine: you install .NET, PostgreSQL, Docker, whatever you choose.
>
> This is the same distinction that settled [008](../decisions/008-database.md): on a VPS,
> nothing external forces MySQL on us, which is why PostgreSQL was freely available as a
> choice.

> ⚠️ **Do this on a machine you'll keep.** The SSH key you generate in step 2 is how you get
> in. Lose it and you're locked out of your own server.

---

## 0b. The domain — register this first

Step 8 needs it, and [037](../decisions/037-platform-hosting.md) makes it effectively
permanent.

### Why "permanent" is stronger here than usual

The domain appears in Steam OpenID return URLs, Discord OAuth redirect URIs, TLS
certificates, every link ever pasted into Discord or a forum
([018](../decisions/018-frontend-architecture.md)) — **and in the plugin callback URL
configured on every organiser's game server** ([007](../decisions/007-server-hosting-model.md)).

Changing it later means **every organiser has to reconfigure their server.** Choose once.

### TLD

| | Verdict |
| --- | --- |
| **`.com`** | ✅ **Chosen.** Universal across pl/en/ru/de, most trusted, cheapest |
| `.pl` | Signals Polish-only — contradicts four-locale support and "reach as many communities as possible" |
| `.gg` | Esports-flavoured, but ~$70/year |
| `.eu` | Cheap, weaker recognition |

Consider registering `.pl` **defensively** — not to use, but so nobody else runs
`nextskill.pl` in the home market.

### ⚠️ Status (checked 2026-08-10): `nextskill.com` and `.eu` are aftermarket, ~€200 each

Both are already registered and listed for resale. That changes the decision:

| | |
| --- | --- |
| **Cost shape** | €200 is a **one-off**, not recurring. Hosting is ~€60/year, so it is roughly three years of infrastructure — paid once, kept forever |
| **Why "buy cheap now, upgrade later" is NOT available** | The domain is permanent (above). Switching later forces **every organiser to reconfigure their game server**. So the choice is: commit to the right name, or pick a cheap one you would genuinely keep forever — not a placeholder |
| **Timing** | **Nothing before Phase 6 needs a real domain.** Development runs on `localhost`. Buying at deployment time costs nothing extra and buys weeks to decide |
| **Negotiation** | Aftermarket asking prices are usually negotiable; offers of 40–60% are routinely accepted. A domain parked at €200 is unlikely to sell in the meantime |
| **If paying: `.com`, not `.eu`** | Same price, but `.com` is universal across four locales while `.eu` is regional. Paying premium for the lesser asset makes no sense |

**Cheaper alternatives worth checking first:** `nextskill.gg` (esports-native, but ~$70 **per
year** — exceeds €200 within three years), `nextskill.pro`, `nextskill.app`,
`playnextskill.com`.

### Registrar: Porkbun

| Registrar | Register | Renew | Note |
| --- | --- | --- | --- |
| **Porkbun** | ~$11 | **~$11** | Same price forever, free WHOIS privacy |
| Cloudflare | **cannot register new domains** | ~$10.46 | Transfer target later; saves ~$0.62/yr — not worth the hassle |
| Namecheap | cheap year 1 | **$18.48** | Discounts year one and recovers it at renewal |

### Settings to enable immediately

| Setting | Why |
| --- | --- |
| **Auto-renew ON** | An expired domain is the single worst self-inflicted outage. Someone else can register it |
| **WHOIS privacy** | Free at Porkbun. Otherwise your name, address and phone number are publicly queryable |
| **Registrar lock ON** | Blocks unauthorised transfer attempts |
| **2FA on the registrar account** | That account controls the domain, which controls the TLS certs and the OAuth redirects. It is the highest-value account in the project — higher than the server |

> *Plain version:* whoever controls the domain can point it anywhere and obtain valid
> certificates for it. That makes the registrar login more security-critical than the VPS:
> losing the server costs you a rebuild; losing the domain costs you the platform.

---

## 1. Create the account and a project

1. Sign up at **console.hetzner.cloud**
2. New accounts sometimes need ID verification — this can take a few hours. Start it early.
3. Create a **project** called `nextskill`.

> *Plain version:* a **project** is just a folder for servers, firewalls and keys. It keeps
> billing and access grouped. One project per product is the sane default.

---

## 2. Generate an SSH key (on your Windows machine)

**Do this before creating the server**, so you can attach the key at creation time.

```powershell
ssh-keygen -t ed25519 -C "nextskill-deploy"
# Press Enter for the default path (C:\Users\miron\.ssh\id_ed25519)
# Set a passphrase. Yes, really — see below.
```

Then copy the **public** half:

```powershell
Get-Content $env:USERPROFILE\.ssh\id_ed25519.pub
```

> *Plain version:* an **SSH key** is a matched pair of files. The **public** key goes on the
> server; the **private** key never leaves your machine. The server sends a challenge only
> the private key can answer, so **nothing secret ever crosses the network** — unlike a
> password, which does, every single time.
>
> The passphrase encrypts the private key **on disk**. If your laptop is stolen, the thief
> has a useless file instead of your server.

**Never share, paste or commit the file without `.pub`.** That one is the actual key.

---

## 3. Create the server

In the Hetzner console → **Add Server**:

| Setting | Value | Why |
| --- | --- | --- |
| Location | **Falkenstein** | EU (GDPR — [011](../decisions/011-accounts-and-login.md)); ~20–30 ms to Poland, which is imperceptible behind Cloudflare |
| Image | **Ubuntu 24.04 LTS** | LTS = 5 years of security updates. Do **not** pick a non-LTS release for a server |
| Type | **Shared vCPU → Arm64 → CAX11** | [042](../decisions/042-hosting.md) |
| Networking | IPv4 **and** IPv6 | Some players will be IPv6-only |
| SSH key | **paste your public key** | So the server never has a password to guess |
| Firewall | create one — see step 4 | |
| Name | `nextskill-prod` | Future you will thank present you |

**Do not** enable a root password. A key-only server cannot be brute-forced.

---

## 4. Firewall — default deny

Create a Hetzner Cloud Firewall (free, and it sits *in front of* the server, so bad traffic
never reaches it).

**Inbound rules — only these:**

| Port | Source | Why |
| --- | --- | --- |
| `22` (SSH) | **your home IP only**, if it's static | The single most-attacked port on the internet |
| `80`, `443` | **Cloudflare IP ranges only** — see step 8 | |

Everything else: **denied**. Outbound: allow all.

> *Plain version:* a firewall that **denies by default** and allows a named few is
> fundamentally safer than one that allows everything and blocks known-bad. You can only
> block threats you've thought of; you can allow the handful of things you actually need.

If your home IP changes (most Polish ISPs), leave SSH open to all **but** complete step 6 —
key-only auth means an open SSH port is an annoyance, not a vulnerability.

---

## 5. First login

```powershell
ssh root@<your-server-ip>
```

Then immediately:

```bash
apt update && apt upgrade -y
timedatectl set-timezone Europe/Warsaw
```

> *Plain version:* a freshly-created image is already weeks old. The first `upgrade` closes
> everything patched since the image was built. Do it before the box is reachable by anything
> that matters.

---

## 6. Create a non-root user and lock SSH down

```bash
adduser miron                      # sets a password — used for sudo, not for SSH
usermod -aG sudo miron
rsync --archive --chown=miron:miron ~/.ssh /home/miron
```

Now edit `/etc/ssh/sshd_config`:

```bash
nano /etc/ssh/sshd_config
```

Set these three:

```
PermitRootLogin no
PasswordAuthentication no
PubkeyAuthentication yes
```

```bash
systemctl restart ssh
```

> ⚠️ **Open a second terminal and confirm `ssh miron@<ip>` works BEFORE closing this one.**
> If you get it wrong and close your only session, you're locked out and rebuilding.

> *Plain version:* `PermitRootLogin no` means an attacker must guess a **username and** a key
> — and `root` is the one username they always know. `PasswordAuthentication no` removes
> brute-forcing entirely: no password exists to guess.

---

## 7. Basic hardening

```bash
# Automatic security updates — patches land without you remembering
apt install -y unattended-upgrades
dpkg-reconfigure --priority=low unattended-upgrades

# fail2ban — bans IPs that repeatedly fail auth
apt install -y fail2ban
systemctl enable --now fail2ban

# 2 GB swap — 4 GB RAM with Postgres + .NET benefits from headroom
fallocate -l 2G /swapfile && chmod 600 /swapfile && mkswap /swapfile && swapon /swapfile
echo '/swapfile none swap sw 0 0' >> /etc/fstab
```

> *Plain version:* **swap** is disk space used as overflow when RAM fills. It's much slower
> than RAM, so it isn't a substitute — but it turns "the process was killed" into "the
> process got slow for a moment", which is a far better failure.

---

## 8. Cloudflare in front — and locking the origin

This is the part that matters most for [040](../decisions/040-threat-model.md).

1. Add your domain to Cloudflare (free plan), change the nameservers at your registrar.
2. Create an **A record** → your server IP, with the **proxy (orange cloud) ON**.
3. SSL/TLS mode: **Full (strict)**.
4. **Then restrict ports 80/443 to Cloudflare's IP ranges** in the Hetzner firewall
   (published at `cloudflare.com/ips`).

> *Plain version:* the orange cloud means visitors reach **Cloudflare**, and Cloudflare
> reaches you. Your server's real IP stops being public, so attacks hit Cloudflare's capacity
> instead of your 4 GB box.
>
> **But that only holds if the origin refuses everyone else.** If your server still accepts
> traffic from any IP, anyone who learns the address bypasses the proxy entirely and hits you
> directly — and the protection you thought you had is decorative. **Step 4 is the step
> people skip.**

---

## 9. Docker

```bash
curl -fsSL https://get.docker.com | sh
usermod -aG docker miron
```

Log out and back in for the group to apply.

> ⚠️ **ARM check.** This box is ARM64. Every image in the compose file must publish an
> `arm64` tag. `postgres`, `caddy` and the .NET runtime images all do — verify anything else
> before relying on it ([042](../decisions/042-hosting.md)).

---

## 10. Backups — do this before you need it

Two layers, and they are not the same thing:

```bash
# Hetzner snapshots: whole-machine, ~20% of server cost, enable in the console.
# Good for "I broke the OS". NOT a database backup.
```

Then a real database dump, **off the host** ([017](../decisions/017-deployment-and-cicd.md)):

```bash
# nightly pg_dump → object storage or another machine
docker exec nextskill-db pg_dump -U nextskill nextskill | gzip > /backup/ns-$(date +%F).sql.gz
```

**Where to put them:** R2's free tier is 10 GB and is already earmarked for demos
([024](../decisions/024-demos.md)), so a growing archive of nightly dumps will outgrow it.
A **Hetzner Storage Box** (~€3.81/month for 1 TB) is the obvious target — different machine,
different failure domain, mountable over SFTP. Not needed until there is real data to lose.

> **A backup you have never restored is a hope, not a backup.** Schedule one restore drill
> into a scratch database and actually run it. The database *is* the product
> ([017](../decisions/017-deployment-and-cicd.md)) — losing it means losing every tournament,
> rating and account.

---

## Checklist

- [ ] Server created, ARM CAX11, Falkenstein, Ubuntu LTS
- [ ] SSH key works; **root login and password auth disabled**
- [ ] Firewall default-deny; 22 restricted or key-only; 80/443 **Cloudflare ranges only**
- [ ] `unattended-upgrades` and `fail2ban` running
- [ ] Swap active
- [ ] Cloudflare proxying, SSL **Full (strict)**, origin IP not public
- [ ] Docker installed, ARM64 images confirmed
- [ ] Nightly dump off-host, **and one restore actually tested**

## Cost

| Item | Monthly |
| --- | --- |
| CAX11 | ~€3.79 |
| Snapshots (optional) | ~€0.80 |
| Cloudflare free + R2 free tier | €0 |
| **Total** | **~€4–5** |
