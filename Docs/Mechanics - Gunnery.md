# Turret Control

Players have the choice between two different forms of turret control: Manual and Automatic

- **Automatic:** Players left-click somewhere on the screen and the turrets will automatically slew to aim at that position, and attempt to raise the guns to the necessary elevation, or the elevation for maximum range if the spot is outside of their maximum range. The Automatic FCS system confers a penalty to turret accuracy when used.

- **Manual:** Players use the keyboard to manually aim the turrets, using Q/E to slew turrets towards Port/Starboard, A/D to move all turrets clockwise or counter-clockwise, and W/S to change the elevation. Space fires the turrets that are selected. R and T toggles between primary and secondary turret groups. Z and X switched between front and aft turrets for the selected group. C reselects all turrets in the selected group.

# Ballistics

Projectiles have travel time, are affected by gravity, and are otherwise simulated properly, not hit-scanned. This requires players to take travel time into account when aiming at targets, especially when using long range weapons.

Players have various types of ammo at their disposal. Primarily, they will have HE and AP options when attacking other surface ships, and which is the proper type depends on what they are going up against. They also have AA (proximity-fused) rounds for anti-air guns or Dual Purpose turrets.

Turret statistics determine things like muzzle velocity, reload rate, turret rotation/slew speed, min/max elevation angles, and what ammo types are compatible with it.

Ammunition statistics will determine damage capability, armor penetration capability, shell weight (which is a component of maximum range), 

### Hit Detection and Damage

Ships have a set number of hit points, determined by the ship class being sailed. Damage from impacts are determined by impact angle, shell size, and ammo type, mitigated by a ship's armor thickness and sailor statistics.

## Ship Armor

Players have the freedom to allocate armor thickness between Deck Armor, Belt Armor, Bulge, and Bulkheads. As long as there is available displacement on their ship, they can add as much armor as they desire.

Armor costs money to add, but refunds are 100%, giving players freedom to experiment and change their armor allocations as desired.

More armor means reduced speed.

#### **Armor Types**

Deck armor helps prevent damage from high-angle impacts, common from long-range shots from Battleships and Heavy Cruisers.

Belt armor prevents damage from direct-fire, low-angle shots, typical from smaller vessels but also from close-range shots from large guns.

Bulge prevents damage from torpedoes and mines.

Bulkhead is a secondary armor type that helps prevent flooding from damage, especially those shots near the water-line, torpedoes, and mines, and allows a ship to take more hits without suffering from excessive speed loss or sinking risk from the damage.

The effectiveness of armor varies marginally by nation: 

- Britain's armor tends to be the most effective, but the most expensive. 
- German armor is light but less effective, except for extremely effective bulkheads. 
- US armor is average all around, but cheaper in general. 
- Japanese deck and belt armor is expensive, but their bulge armor is cheap and highly effective.

#### **Soft Armor**

Highly-skilled support sailors, specifically Damage Control and Repairmen, prevent damage by putting out fires that are started by impacts. 

Fires cause damage over time and can cause ammo rack detonations if left unchecked. Restorers are able to repair damage done over time, to an extent. 

Veteran sailors in sailor units also all contribute a small amount of 'soft armor' to the ship's health pool, which is an invisible amount of extra health that a ship has, which is removed first prior to damage being subtracted from the main, visible health pool a ship has. 

This soft armor is not displayed outright to a player, but its effect is present nonetheless.

