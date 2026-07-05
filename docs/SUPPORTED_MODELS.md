# Supported models

> Auto-generated from [`Devices.cs`](../Devices.cs) - the single source of truth. Do not edit by hand; regenerate when models change.

**135 firmware IDs** are recognised: **3 tested** on real hardware (MSI Raider GE78HX / Vector GP78HX 13V/14V; MSI Crosshair A16 HX D7W/D8W; MSI Sword 16 HX B13V/B14V) and **132 experimental** (opt-in), built from the [msi-ec](https://github.com/BeardOverflow/msi-ec) register maps and cross-checked against [MControlCenter](https://github.com/dmitry-s93/MControlCenter). On an **unrecognised firmware the app stays read-only** (Status works, no writes), so it never touches wrong registers.

Column meaning:

- **Family** - EC register layout. **G2** = shift `0xD2` / fan `0xD4` / super-batt `0xEB` / charge `0xD7` (same as the tested board). **G1** = shift `0xF2` / fan `0xF4` / charge `0xEF`, older boards.
- **Status** - &#9989; tested = verified on hardware; &#9887;&#65039; experimental = documented registers, not yet confirmed by an owner (the low-power "Silent" behaviour in particular).
- **Fan curve** - &#9989; editable = the curve tab writes the curve; &#9673; unverified = editable once Experimental is enabled, but the table addresses (CPU `0x6A`/`0x72`, GPU `0x82`/`0x8A`, shared across the G2 family by MControlCenter) are not yet confirmed on that exact model - compare with MSI Center first; &mdash; = no curve support (profiles only).
- **Super Battery** - whether the model exposes a super-battery throttle register.
- **RPM** - whether the fan-tachometer registers are known (so real fan RPM is shown), with their addresses. Verified only where hardware/dumps confirmed them.

Own an experimental model and can confirm it works (or doesn't)? Use the in-app **Report my model...** wizard (tray menu / Status window) or open a [Model support request](../../../issues/new?template=model-support.yml).

| Model | EC firmware | Family | Status | Fan curve | Super Battery | RPM |
|---|---|---|---|---|---|---|
| MSI Raider GE78HX 13V / 14V | `17S1IMS1`, `17S2IMS2` | G2 | &#9989; tested | &#9989; editable | &#10003; | &#10003; 0xC9/0xCB |
| MSI Crosshair A16 HX (D7W/D8W) | `15PLIMS1` | G2 | &#9989; tested | &#9673; unverified | &mdash; | &#10003; 0xC9/0xCB |
| MSI Sword 16 HX B13V / B14V | `15P2EMS1` | G2 | &#9989; tested | &#9673; unverified | &#10003; | &mdash; |
| MSI Alpha 17 C7VF / C7VG | `17KKIMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Bravo 15 B7ED | `158PIMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Bravo 15 C7V | `158NIMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Bravo 17 C7VE | `17LNIMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Creator 17 B11UE | `17M1EMS2` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Creator M16 B13VF | `1585EMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Creator Z16 A11UE | `1571EMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Creator Z16 A12U | `1572EMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Creator Z17 A12UGST | `17N1EMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Crosshair 15 B12UEZ / B12UGSZ | `1583EMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Crosshair 16 HX AI D2XW | `15P4EMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Crosshair 17 B12UGZ | `17L3EMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Cyborg 14 A13VF | `14P1IMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Cyborg 15 A12VF | `15K1IMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Cyborg 15 AI A1VFK | `15K2EMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI GE66 Raider / GP66 Leopard | `1543EMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI GE76 Raider 11U / 11UH | `17K3EMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI GF63 Thin 11UC / 11SC | `16R6EMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI GS66 Stealth 11UE / 11UG | `16V4EMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Katana 15 B12VEK / B12VFK / B12VGK | `1585EMS2` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Katana 15 HX B14WEK | `1587EMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Katana 17 B12UCXK | `17L5EMS2` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Katana 17 HX B14WGK | `17L7EMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Katana GF66 | `1582EMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Katana GF66 11UE / 11UG | `1581EMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Katana GF66 12U | `1584EMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Katana GF66 12UDO | `1584IMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Katana GF76 | `17L1EMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Katana GF76 11UC / 11UD | `17L2EMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Katana GF76 12UC | `17L4EMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Modern 14 B11M | `14D2EMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Modern 14 B11MOU | `14D3EMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Modern 14 C12M | `14J1IMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Modern 14 H D13M | `14L1EMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Modern 15 A11M | `1552EMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Modern 15 B12HW | `15H2IMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Modern 15 B13M | `15H1IMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Modern 15 H AI C1MG | `15H5EMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Prestige 13 AI Evo A1MG | `13Q2EMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Prestige 13 AI+ Evo A2VMG | `13Q3EMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Prestige 14 A11SCX | `14C4EMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Prestige 14 AI Evo C1MG | `14N1EMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Prestige 14 AI Studio C1UDXG | `14N2EMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Prestige 14 Evo A12M | `14C6EMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Prestige 15 A11SCX | `16S6EMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Prestige 15 A12SC / A12UC | `16S8EMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Prestige 16 AI Evo B1MG | `15A1EMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Prestige 16 AI+ Evo B2VMG | `15A3EMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Prestige 16 Studio A13VE | `1594EMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Prestige A16 AI+ A3HMG | `159KIMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Pulse 16 AI C1VGKG/C1VFKG | `15P3EMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Pulse/Katana 17 B13V/GK | `17L5EMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Raider A18 HX A7VIG | `182KIMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Raider GE67 HX 12U | `1545IMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Raider GE68 HX 14VGG | `15M2IMS2` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Raider GE68 HX 14VIG | `15M1IMS2` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Raider GE68HX 13V | `15M2IMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Raider GE76 12UE | `17K4EMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Raider GE77 HX 12UGS | `17K5IMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Raider GE78 HX 14VHG | `17S1IMS2` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Raider GE78 HX Smart Touchpad 13V | `17S2IMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Stealth 14 AI Studio A1VGG / A1VFG | `14K2EMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Stealth 14 Studio A13VF | `14K1EMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Stealth 15 A13V | `16V6EMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Stealth 15M A11SEK | `1562EMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Stealth 15M A11UEK | `1563EMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Stealth 16 AI A2HWFG | `15F5EMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Stealth 16 AI Studio A1VFG | `15F4EMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Stealth 16 AI Studio A1VHG | `15F3EMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Stealth 16 Studio A13VG | `15F2EMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Stealth 17 Studio A13VI | `17P2EMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Stealth A16 AI+ A3XVFG / A3XVGG | `15FKIMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Stealth A16 AI+ A3XWHG | `15FLIMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Stealth A16 Mercedes AMG AI+ A3XWGG | `15FMIBA1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Stealth GS66 12UE / 12UGS | `16V5EMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Stealth GS76 11UG | `17M1EMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Stealth GS77 12U | `17P1EMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Summit 13 AI+ Evo A2VM | `13P5EMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Summit E13 Flip A12MT | `13P3EMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Summit E14 Flip Evo A12MT | `14F1EMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Summit E16 AI Studio A1VETG | `1596EMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Summit E16 Flip A11UCT | `1591EMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Summit E16 Flip A12UCT / A12MT | `1592EMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Sword 17 HX B14VGKG | `17T2EMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Thin 15 B12UCX / B12VE | `16R8IMS2` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Thin A15 B7VF | `16RKIMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Thin A15 B7VF | `16RKIMS2` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Thin GF63 12HW | `16R7IMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Thin GF63 12VE | `16R8IMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Titan 18 HX A14V | `1822EMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Titan 18 HX Dragon Edition | `1824EMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Titan GT77 12UHS | `17Q1IMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Titan GT77HX 13VH | `17Q2IMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Vector 16 HX AI A2XWHG / A2XWIG | `15M3EMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Vector 17 HX AI A2XWHG | `17S3EMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Vector A18 HX A9WHG | `182LIMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Vector GP66 12UGS | `1544EMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Vector GP68 HX 13V | `15M1IMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Venture A14 AI+ A3HMG | `14QKIMS1` | G2 | &#9887;&#65039; experimental | &#9673; unverified | &#10003; | &mdash; |
| MSI Alpha 15 B5EE / B5EEK | `158LEMS1` | G1 | &#9887;&#65039; experimental | &mdash; | &mdash; | &mdash; |
| MSI Alpha 17 B5EEK | `17LLEMS1` | G1 | &#9887;&#65039; experimental | &mdash; | &mdash; | &mdash; |
| MSI Bravo 15 A4DDR | `16WKEMS1` | G1 | &#9887;&#65039; experimental | &mdash; | &mdash; | &mdash; |
| MSI Bravo 15 B5DD | `158KEMS1` | G1 | &#9887;&#65039; experimental | &mdash; | &mdash; | &mdash; |
| MSI Bravo 15 B5ED | `158MEMS1` | G1 | &#9887;&#65039; experimental | &mdash; | &mdash; | &mdash; |
| MSI Bravo 17 A4DDR / A4DDK | `17FKEMS1` | G1 | &#9887;&#65039; experimental | &mdash; | &mdash; | &mdash; |
| MSI Creator 15 A10SD | `16V2EMS1` | G1 | &#9887;&#65039; experimental | &mdash; | &#10003; | &mdash; |
| MSI Delta 15 A5EFK | `15CKEMS1` | G1 | &#9887;&#65039; experimental | &mdash; | &mdash; | &mdash; |
| MSI GE66 Raider 10SF | `1541EMS1` | G1 | &#9887;&#65039; experimental | &mdash; | &mdash; | &mdash; |
| MSI GF63 8RC-249 | `16R1EMS1` | G1 | &#9887;&#65039; experimental | &mdash; | &mdash; | &mdash; |
| MSI GF63 Thin 10SCX / 10SCS | `16R4EMS1` | G1 | &#9887;&#65039; experimental | &mdash; | &mdash; | &mdash; |
| MSI GF63 Thin 10U / 10SC | `16R5EMS1` | G1 | &#9887;&#65039; experimental | &mdash; | &mdash; | &mdash; |
| MSI GF63 Thin 9SC | `16R3EMS1` | G1 | &#9887;&#65039; experimental | &mdash; | &mdash; | &mdash; |
| MSI GF63 Thin 9SCSR | `16R4EMS2` | G1 | &#9887;&#65039; experimental | &mdash; | &mdash; | &mdash; |
| MSI GF65 Thin | `16W2EMS1` | G1 | &#9887;&#65039; experimental | &mdash; | &mdash; | &mdash; |
| MSI GF65 Thin 10SCSXR / 10SD / 10SE | `16W1EMS2` | G1 | &#9887;&#65039; experimental | &mdash; | &mdash; | &mdash; |
| MSI GF65 Thin 9SE / 9SD | `16W1EMS1` | G1 | &#9887;&#65039; experimental | &mdash; | &mdash; | &mdash; |
| MSI GP66 Leopard 10UG / 10UE / 10UH | `1542EMS1` | G1 | &#9887;&#65039; experimental | &mdash; | &mdash; | &mdash; |
| MSI GS65 Stealth | `16Q4EMS1` | G1 | &#9887;&#65039; experimental | &mdash; | &mdash; | &mdash; |
| MSI GS65 Stealth Thin 8RE / 8RF | `16Q2EMS1` | G1 | &#9887;&#65039; experimental | &mdash; | &mdash; | &mdash; |
| MSI GS66 Stealth | `16V1EMS1` | G1 | &#9887;&#65039; experimental | &mdash; | &mdash; | &mdash; |
| MSI GS66 Stealth 10UE | `16V3EMS1` | G1 | &#9887;&#65039; experimental | &mdash; | &mdash; | &mdash; |
| MSI Modern 14 B10MW | `14D1EMS1` | G1 | &#9887;&#65039; experimental | &mdash; | &mdash; | &mdash; |
| MSI Modern 14 B4MW | `14DKEMS1` | G1 | &#9887;&#65039; experimental | &mdash; | &mdash; | &mdash; |
| MSI Modern 14 B5M | `14DLEMS1` | G1 | &#9887;&#65039; experimental | &mdash; | &mdash; | &mdash; |
| MSI Modern 14 C5M | `14JKEMS1` | G1 | &#9887;&#65039; experimental | &mdash; | &mdash; | &mdash; |
| MSI Modern 15 A10M | `1551EMS1` | G1 | &#9887;&#65039; experimental | &mdash; | &mdash; | &mdash; |
| MSI Modern 15 A5M | `155LEMS1` | G1 | &#9887;&#65039; experimental | &mdash; | &mdash; | &mdash; |
| MSI Modern 15 B7M | `15HKEMS1` | G1 | &#9887;&#65039; experimental | &mdash; | &mdash; | &mdash; |
| MSI P65 Creator 8RE | `16Q3EMS1` | G1 | &#9887;&#65039; experimental | &mdash; | &mdash; | &mdash; |
| MSI Prestige 14 A10SC | `14C1EMS1` | G1 | &#9887;&#65039; experimental | &mdash; | &mdash; | &mdash; |
| MSI Prestige 15 A10SC | `16S3EMS1` | G1 | &#9887;&#65039; experimental | &mdash; | &mdash; | &mdash; |
| MSI PS63 MODERN 8RD | `16S1EMS1` | G1 | &#9887;&#65039; experimental | &mdash; | &mdash; | &mdash; |

