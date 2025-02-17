Izzy Manual Testing Script:

Note that many of these steps require an alt account (so that your main account can put it back into a normal state afterward) and suitable .conf files (so there's enough data to test with).

Because this is a *manual* testing script, the goal is not be comprehensive coverage (or else we'd never get in the habit of always doing all of it). Instead, the goal is to cover a small subset of functionality that can be run through in at most a few minutes, and is either particularly critical or particularly likely to have issues undetectable by automated tests.

- `.echo ack`

- self-docs
  - `.help`
  - `.config`
  - `.config ModRole`
  - `.config ModChannel`
  - `.config MentionResponses list`
  - `.config FilterIgnoredChannels list`
  - `.config FilterBypassRoles list`
  - `.config Aliases list`
  - `.config FilteredWords list`
  - run `.config` with your alt, which should do nothing

- new user management and scheduling
  - make your alt leave the server, which should post a mod log
  - then reinvite it (or use an existing invite), which should post another modlog
  - `.remindme 1 minute this is part of the test`, which should DM you in a minute
  - `.schedule list`, which should show a role removal task and an echo task
  - `.assignrole <@&1039194817231601695> <your alt> 10 seconds`, which should remove new-member-role much sooner

- filtering
  - trip the filter with your alt (without a FilterBypassRoles role), which should silence the user, delete the message, post a message telling them off, post a modlog with embed and ping mods
  - manually give your alt back the member role
  - `.config FilterBypassRoles add <@&965978050229571634>`
  - now trip the filter again (with a FilterBypassRoles role), which should post a modlog with no pings or silencings
  - `.config FilterBypassRoles remove <@&965978050229571634>`
  - make your alt post the hardcoded spam test string to deterministically cause spam: `=+i7B3s+#(-{×jn6Ga3F~lA:IZZY_PRESSURE_TEST:H4fgd3!#!` which should delete the message, silence your alt, post a mod log and a bulk deletion log

- quotes
  - `.q`
  - `.lq`
  - `.lq <user/category>` with no quotes
  - `.lq <user/category>` with few quotes
  - `.lq <user/category>` with enough quotes to paginate

- `.ban <your alt> 10 seconds`, which should remove your alt from the server, post messages and mod logs, then later allow the alt to rejoin

- small raids (you'd need at least two alts to test a large raid)
	- make sure your test values for `.config SmallRaidDecay` and `.config RecentJoinDecay` are low (e.g. `1` and `10`), so this will involve less waiting, and that `.config RaidProtectionEnabled` is `true`
	- run `.config SmallRaidSize 1`
	- make your alt (re)join, which should post a raid message
	- wait 1 minute for the "raid" to end, which should post another message
	- either do `.config RaidProtectionEnabled false` or change `.config SmallRaidSize` back to a more reasonable value

- `.wipe <#963788928702373918> 20 minutes`, which should delete most of the messages you just produced, post a bulk deletion log and link to it

(we don't have a great way to repeatedly test raids right now; even the `.test raid` command requires seveal real user ids which would then get silenced and that's a mess to clean up)
